using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.Embedding;
using SqlMind.Infrastructure.Persistence;
using SqlMind.Infrastructure.RAG;

namespace SqlMind.IntegrationTests;

/// <summary>
/// Live integration tests for the RAG pipeline.
/// Tests are skipped (not failed) when GEMINI_API_KEY or DATABASE_URL are absent,
/// using xUnit's [Fact(Skip = ...)] via the SkipIfMissing helper.
/// </summary>
public sealed class RagLiveTests
{
    private static readonly string? ApiKey      = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? LoadFromDotEnv("GEMINI_API_KEY");
    private static readonly string? DatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")   ?? LoadFromDotEnv("DATABASE_URL");

    // ── Embedding tests ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GeminiEmbedding_Returns3072DimensionalVector()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "GEMINI_API_KEY not set — skipping.");

        var svc = BuildEmbeddingService();

        float[] vector;
        try
        {
            vector = await svc.EmbedAsync("SELECT * FROM orders WHERE status = 'pending'");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("404"))
        {
            // 429 = quota exhausted; 404 = model not available for this API key tier — not a code defect
            Skip.If(true, $"Gemini Embedding skipped: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
            return;
        }

        Assert.Equal(3072, vector.Length);
        Assert.Equal(3072, svc.Dimensions);
        Assert.True(vector.Any(v => v != 0f), "Embedding vector must not be all zeros.");
    }

    [SkippableFact]
    public async Task GeminiEmbedding_Batch_AllVectors3072Dimensions()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "GEMINI_API_KEY not set — skipping.");

        var svc = BuildEmbeddingService();
        var texts = new List<string>
        {
            "DROP TABLE users",
            "UPDATE orders SET status='shipped' WHERE id = 1",
            "SELECT id, name FROM products LIMIT 10"
        };

        List<float[]> vectors;
        try
        {
            vectors = await svc.EmbedBatchAsync(texts);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("404"))
        {
            Skip.If(true, $"Gemini Embedding skipped: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
            return;
        }

        Assert.Equal(texts.Count, vectors.Count);
        Assert.All(vectors, v => Assert.Equal(3072, v.Length));
    }

    // ── RAG index + retrieve tests ────────────────────────────────────────────

    [SkippableFact]
    public async Task RagService_IndexThenRetrieve_ReturnsRelevantChunk()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey),     "GEMINI_API_KEY not set — skipping.");
        Skip.If(string.IsNullOrWhiteSpace(DatabaseUrl), "DATABASE_URL not set — skipping.");

        var db  = BuildDbContext();
        var svc = BuildRagService(db);

        var uniqueMarker = Guid.NewGuid().ToString("N")[..8];
        var document = new KnowledgeDocument
        {
            Title   = $"Test document {uniqueMarker}",
            Content = $"When performing a DELETE without a WHERE clause on table orders_{uniqueMarker}, " +
                      $"all rows will be removed permanently. Always verify row counts before bulk deletes. " +
                      $"Use transactions and test in staging before production. Marker: {uniqueMarker}.",
            Source  = "integration-test"
        };

        try
        {
            await svc.IndexDocumentAsync(document);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("404"))
        {
            Skip.If(true, $"Gemini Embedding skipped: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
            return;
        }

        var context = await svc.RetrieveAsync($"DELETE without WHERE orders_{uniqueMarker}");

        Assert.True(context.WasUsed);
        Assert.NotEmpty(context.RetrievedChunks);
        Assert.NotEmpty(context.AssembledContext);
        Assert.Contains(uniqueMarker, context.AssembledContext);
        Assert.All(context.Scores, s => Assert.True(s >= 0f && s <= 1.01f));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeminiEmbeddingService BuildEmbeddingService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GEMINI_API_KEY"] = ApiKey })
            .Build();

        return new GeminiEmbeddingService(
            new HttpClient(),
            config,
            NullLogger<GeminiEmbeddingService>.Instance);
    }

    private static SqlMindDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlMindDbContext>()
            .UseNpgsql(DatabaseUrl, npgsql => npgsql.UseVector())
            .Options;
        return new SqlMindDbContext(options);
    }

    private static RagService BuildRagService(SqlMindDbContext db) =>
        new(db, BuildEmbeddingService(), NullLogger<RagService>.Instance);

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
