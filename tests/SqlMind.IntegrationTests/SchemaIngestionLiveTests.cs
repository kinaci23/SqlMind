using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Infrastructure.Embedding;
using SqlMind.Infrastructure.Persistence;
using SqlMind.Infrastructure.RAG;
using SqlMind.Infrastructure.Schema;

namespace SqlMind.IntegrationTests;

/// <summary>
/// Live integration tests for SchemaIngestionService.
/// Requires a running PostgreSQL instance at localhost:5433 (docker-compose.yml).
///
/// All tests are skipped automatically if DATABASE_URL is not set,
/// so the CI pipeline stays green without Docker.
/// </summary>
public sealed class SchemaIngestionLiveTests
{
    // Default dev connection string matching docker-compose.yml
    private static readonly string DefaultConnStr =
        "Host=localhost;Port=5433;Database=sqlmind;Username=postgres;Password=sqlmind123";

    private static readonly string? DatabaseUrl =
        Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? LoadFromDotEnv("DATABASE_URL");

    // Use the env var if set, otherwise fall back to the default dev connection string.
    // Tests still skip when Docker is not reachable (connection refused → Skip).
    private static string ConnStr => DatabaseUrl ?? DefaultConnStr;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task IngestAsync_SqlMindDb_CreatesDocumentsForEachTable()
    {
        await using var db = BuildDbContext();

        // Verify connectivity; skip gracefully if Docker is not running.
        if (!await CanConnectAsync())
        {
            Skip.If(true, "PostgreSQL at localhost:5433 is not reachable — skipping.");
            return;
        }

        var rag = new RagService(db, BuildEmbeddingService(), NullLogger<RagService>.Instance);
        var sut = new SchemaIngestionService(rag, db, NullLogger<SchemaIngestionService>.Instance);

        var result = await sut.IngestAsync(ConnStr, "integration-test");

        Assert.NotNull(result);
        Assert.NotEmpty(result.TablesIngested);
        Assert.True(result.DocumentsCreated > 0);
        Assert.Equal(result.TablesIngested.Count, result.DocumentsCreated);
        Assert.Equal("integration-test", result.Environment);

        // Verify documents were actually persisted to knowledge_documents
        var docCount = await db.KnowledgeDocuments
            .CountAsync(d => d.Source == "schema-ingestion" && d.Title.Contains("integration-test"));

        Assert.True(docCount > 0, "Expected at least one knowledge document for integration-test environment.");
    }

    [SkippableFact]
    public async Task IngestAsync_SqlMindDb_KnownTablesPresent()
    {
        await using var db = BuildDbContext();

        if (!await CanConnectAsync())
        {
            Skip.If(true, "PostgreSQL at localhost:5433 is not reachable — skipping.");
            return;
        }

        var rag = new RagService(db, BuildEmbeddingService(), NullLogger<RagService>.Instance);
        var sut = new SchemaIngestionService(rag, db, NullLogger<SchemaIngestionService>.Instance);

        var result = await sut.IngestAsync(ConnStr, "live-test");

        // SqlMind's own schema must contain these tables
        var expected = new[]
        {
            "knowledge_documents", "knowledge_chunks", "embeddings",
            "analysis_jobs", "analysis_results", "audit_logs"
        };

        foreach (var table in expected)
        {
            Assert.Contains(table, result.TablesIngested,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public async Task GetIngestedTablesAsync_AfterIngest_ReturnsIngestedTableNames()
    {
        await using var db = BuildDbContext();

        if (!await CanConnectAsync())
        {
            Skip.If(true, "PostgreSQL at localhost:5433 is not reachable — skipping.");
            return;
        }

        var rag = new RagService(db, BuildEmbeddingService(), NullLogger<RagService>.Instance);
        var sut = new SchemaIngestionService(rag, db, NullLogger<SchemaIngestionService>.Instance);

        var env = $"roundtrip-{Guid.NewGuid():N}";
        var ingestResult = await sut.IngestAsync(ConnStr, env);

        var tables = await sut.GetIngestedTablesAsync(env);

        Assert.NotEmpty(tables);
        // Every table returned by ingest must be retrievable
        foreach (var t in ingestResult.TablesIngested)
            Assert.Contains(t, tables, StringComparer.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SqlMindDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlMindDbContext>()
            .UseNpgsql(ConnStr, npgsql => npgsql.UseVector())
            .Options;
        return new SqlMindDbContext(options);
    }

    private static GeminiEmbeddingService BuildEmbeddingService()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? LoadFromDotEnv("GEMINI_API_KEY")
            ?? "no-key";

        // Build a minimal IConfiguration backed by a plain dictionary.
        var config = new FlatConfiguration(new Dictionary<string, string?> { ["GEMINI_API_KEY"] = apiKey });

        return new GeminiEmbeddingService(
            new HttpClient(),
            config,
            NullLogger<GeminiEmbeddingService>.Instance);
    }

    /// <summary>Minimal IConfiguration backed by a flat string dictionary (no extra NuGet needed).</summary>
    private sealed class FlatConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly Dictionary<string, string?> _data;
        public FlatConfiguration(Dictionary<string, string?> data) => _data = data;

        public string? this[string key]
        {
            get => _data.TryGetValue(key, out var v) ? v : null;
            set => _data[key] = value;
        }

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
            => new FlatSection(key, _data);

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren()
            => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

        private sealed class FlatSection : Microsoft.Extensions.Configuration.IConfigurationSection
        {
            private readonly string _key;
            private readonly Dictionary<string, string?> _data;
            public FlatSection(string key, Dictionary<string, string?> data) { _key = key; _data = data; }

            public string? this[string key]
            {
                get => _data.TryGetValue($"{_key}:{key}", out var v) ? v : null;
                set => _data[$"{_key}:{key}"] = value;
            }
            public string Key  => _key;
            public string Path => _key;
            public string? Value
            {
                get => _data.TryGetValue(_key, out var v) ? v : null;
                set => _data[_key] = value;
            }
            public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => new FlatSection($"{_key}:{key}", _data);
            public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
                => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
        }
    }

    private static async Task<bool> CanConnectAsync()
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(ConnStr);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? LoadFromDotEnv(string key)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    if (line.StartsWith($"{key}=", StringComparison.Ordinal))
                        return line[(key.Length + 1)..].Trim();
                }
            }
            dir = dir.Parent;
        }
        return null;
    }
}
