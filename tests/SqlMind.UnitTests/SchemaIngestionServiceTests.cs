using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.Persistence;
using SqlMind.Infrastructure.Schema;

namespace SqlMind.UnitTests;

/// <summary>
/// Unit tests for SchemaIngestionService.
/// Uses in-process test doubles — no real database or HTTP calls.
///
/// Covers:
///   1. GetIngestedTablesAsync — returns correct table names from knowledge_documents
///   2. GetIngestedTablesAsync — returns empty list when nothing is ingested
///   3. BuildDocument — column section is correctly formatted
///   4. BuildDocument — FK (outgoing) section is correctly formatted
///   5. BuildDocument — FK (incoming) section is correctly formatted
///   6. BuildDocument — index section is correctly formatted
///   7. BuildDocument — omits empty sections (no indexes, no FKs)
/// </summary>
public sealed class SchemaIngestionServiceTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class NullRagService : IRagService
    {
        public List<KnowledgeDocument> Indexed { get; } = [];

        public Task IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct = default)
        {
            Indexed.Add(document);
            return Task.CompletedTask;
        }

        public Task<RagContext> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default)
            => Task.FromResult(new RagContext());

        public Task<bool> ShouldUseRagAsync(SqlMind.Core.Models.SqlParseResult parseResult, SqlMind.Core.Enums.RiskLevel riskLevel)
            => Task.FromResult(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SqlMindDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<SqlMindDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SqlMindDbContext(options);
    }

    private static SchemaIngestionService CreateSut(SqlMindDbContext db, IRagService? ragService = null) =>
        new(ragService ?? new NullRagService(), db, NullLogger<SchemaIngestionService>.Instance);

    // ── GetIngestedTablesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetIngestedTablesAsync_EmptyDb_ReturnsEmptyList()
    {
        await using var db = CreateInMemoryDb(nameof(GetIngestedTablesAsync_EmptyDb_ReturnsEmptyList));
        var sut = CreateSut(db);

        var tables = await sut.GetIngestedTablesAsync("production");

        Assert.Empty(tables);
    }

    [Fact]
    public async Task GetIngestedTablesAsync_WithMatchingDocs_ReturnsTableNames()
    {
        await using var db = CreateInMemoryDb(nameof(GetIngestedTablesAsync_WithMatchingDocs_ReturnsTableNames));

        // Seed two schema-ingestion docs for "staging"
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            Title   = "users tablosu — otomatik şema (staging)",
            Source  = "schema-ingestion",
            Content = "...",
        });
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            Title   = "orders tablosu — otomatik şema (staging)",
            Source  = "schema-ingestion",
            Content = "...",
        });
        // A different environment — should NOT appear
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            Title   = "products tablosu — otomatik şema (production)",
            Source  = "schema-ingestion",
            Content = "...",
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var tables = await sut.GetIngestedTablesAsync("staging");

        Assert.Equal(2, tables.Count);
        Assert.Contains("users",  tables);
        Assert.Contains("orders", tables);
        Assert.DoesNotContain("products", tables);
    }

    [Fact]
    public async Task GetIngestedTablesAsync_NonSchemaIngestionDocs_NotReturned()
    {
        await using var db = CreateInMemoryDb(nameof(GetIngestedTablesAsync_NonSchemaIngestionDocs_NotReturned));

        // Doc added manually by a DBA (source != "schema-ingestion")
        db.KnowledgeDocuments.Add(new KnowledgeDocument
        {
            Title   = "users tablosu — iş kuralları (production)",
            Source  = "dba-manuel",
            Content = "...",
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // "dba-manuel" doc contains "production" in title but source differs
        var tables = await sut.GetIngestedTablesAsync("production");

        Assert.Empty(tables);
    }

    // ── BuildDocument — column formatting ─────────────────────────────────────

    [Fact]
    public void BuildDocument_Columns_FormattedCorrectly()
    {
        var columns = new List<SchemaIngestionService.ColumnInfo>
        {
            new("id",    "integer", false, "nextval('users_id_seq')"),
            new("email", "text",    false, null),
            new("bio",   "text",    true,  null),
        };

        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "users",
            environment: "test",
            rowCount:    42,
            columns:     columns,
            indexes:     [],
            outgoingFks: [],
            incomingFks: []);

        Assert.Contains("Tablo: users",              doc.Content);
        Assert.Contains("Ortam: test",               doc.Content);
        Assert.Contains("Tahmini satır sayısı: 42",  doc.Content);
        Assert.Contains("- id (integer, not null, varsayılan: nextval('users_id_seq'))", doc.Content);
        Assert.Contains("- email (text, not null)",  doc.Content);
        Assert.Contains("- bio (text, nullable)",    doc.Content);
    }

    // ── BuildDocument — outgoing FK formatting ────────────────────────────────

    [Fact]
    public void BuildDocument_OutgoingForeignKeys_FormattedCorrectly()
    {
        var fks = new List<SchemaIngestionService.ForeignKeyInfo>
        {
            new("user_id", "users", "id"),
        };

        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "orders",
            environment: "test",
            rowCount:    0,
            columns:     [new("id", "integer", false, null)],
            indexes:     [],
            outgoingFks: fks,
            incomingFks: []);

        Assert.Contains(
            "Bu tablodan çıkan foreign key'ler (bu tablo başka tablolara bağlı):",
            doc.Content);
        Assert.Contains("- user_id → users.id", doc.Content);
    }

    // ── BuildDocument — incoming FK formatting ────────────────────────────────

    [Fact]
    public void BuildDocument_IncomingForeignKeys_FormattedCorrectly()
    {
        var incomingFks = new List<SchemaIngestionService.IncomingForeignKeyInfo>
        {
            new("orders", "user_id", "id"),
        };

        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "users",
            environment: "test",
            rowCount:    0,
            columns:     [new("id", "integer", false, null)],
            indexes:     [],
            outgoingFks: [],
            incomingFks: incomingFks);

        Assert.Contains(
            "Bu tabloya gelen foreign key'ler (başka tablolar buraya bağlı):",
            doc.Content);
        Assert.Contains("- orders.user_id → users.id", doc.Content);
    }

    // ── BuildDocument — index formatting ─────────────────────────────────────

    [Fact]
    public void BuildDocument_Indexes_FormattedCorrectly()
    {
        var indexes = new List<SchemaIngestionService.IndexInfo>
        {
            new("users_email_idx", "CREATE UNIQUE INDEX users_email_idx ON users(email)"),
        };

        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "users",
            environment: "test",
            rowCount:    0,
            columns:     [new("id", "integer", false, null)],
            indexes:     indexes,
            outgoingFks: [],
            incomingFks: []);

        Assert.Contains("Index'ler:", doc.Content);
        Assert.Contains("- users_email_idx: CREATE UNIQUE INDEX users_email_idx ON users(email)", doc.Content);
    }

    // ── BuildDocument — empty optional sections omitted ───────────────────────

    [Fact]
    public void BuildDocument_NoIndexesNoFks_OmitsOptionalSections()
    {
        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "simple_table",
            environment: "test",
            rowCount:    0,
            columns:     [new("id", "integer", false, null)],
            indexes:     [],
            outgoingFks: [],
            incomingFks: []);

        Assert.DoesNotContain("Index'ler:",              doc.Content);
        Assert.DoesNotContain("foreign key",             doc.Content);
    }

    // ── BuildDocument — source and title ─────────────────────────────────────

    [Fact]
    public void BuildDocument_TitleAndSource_MatchExpectedPattern()
    {
        var doc = SchemaIngestionService.BuildDocument(
            tableName:   "analysis_jobs",
            environment: "production",
            rowCount:    1000,
            columns:     [new("id", "uuid", false, null)],
            indexes:     [],
            outgoingFks: [],
            incomingFks: []);

        Assert.Equal("analysis_jobs tablosu — otomatik şema (production)", doc.Title);
        Assert.Equal("schema-ingestion", doc.Source);
    }
}
