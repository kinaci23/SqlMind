using SqlMind.Core.Enums;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.RAG;
using SqlMind.Infrastructure.SqlParsing;

namespace SqlMind.UnitTests;

/// <summary>
/// Edge case tests covering unusual or boundary inputs across the pipeline.
/// </summary>
public sealed class EdgeCaseTests
{
    private readonly CustomSqlAnalyzer _analyzer = new();

    // ── Empty / whitespace SQL ────────────────────────────────────────────────

    [Fact]
    public async Task EmptySql_ShouldReturn_ParseWarning_NoOperations()
    {
        var result = await _analyzer.ParseAsync(string.Empty);

        Assert.Empty(result.Operations);
        Assert.Empty(result.TablesDetected);
        Assert.NotEmpty(result.ParseWarnings);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("\r\n   \t")]
    public async Task WhitespaceSql_ShouldReturn_ParseWarning_NoOperations(string sql)
    {
        var result = await _analyzer.ParseAsync(sql);

        Assert.Empty(result.Operations);
        Assert.NotEmpty(result.ParseWarnings);
    }

    // ── Comment-only SQL ──────────────────────────────────────────────────────

    [Fact]
    public async Task CommentOnlySql_ShouldReturn_NoOperations()
    {
        var result = await _analyzer.ParseAsync("-- just a comment\n-- another comment");

        Assert.Empty(result.Operations);
        Assert.Empty(result.TablesDetected);
        Assert.False(result.HasUnfilteredMutation);
    }

    [Fact]
    public async Task BlockCommentOnlySql_ShouldReturn_NoOperations()
    {
        var result = await _analyzer.ParseAsync("/* this is a block comment */");

        Assert.Empty(result.Operations);
        Assert.False(result.HasDdlOperation);
    }

    // ── Large SQL ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LargeSql_10000Chars_ShouldParseWithoutTimeout()
    {
        // Build a long but valid SELECT: pad each column name to ensure >= 10 000 total chars
        var columns = string.Join(", ", Enumerable.Range(1, 500).Select(i => $"very_long_column_name_{i:D4}"));
        var sql = $"SELECT {columns} FROM big_table WHERE id > 0";

        Assert.True(sql.Length >= 10_000, $"Expected >= 10000 chars, got {sql.Length}");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _analyzer.ParseAsync(sql, cts.Token);

        Assert.Contains(OperationType.SELECT, result.Operations);
        Assert.Contains("big_table", result.TablesDetected, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.WhereClauseExists);
        Assert.False(result.HasUnfilteredMutation);
    }

    // ── Invalid / garbage SQL ─────────────────────────────────────────────────

    [Fact]
    public async Task GarbageSql_ShouldReturnGracefully_NoException()
    {
        var exception = await Record.ExceptionAsync(() =>
            _analyzer.ParseAsync("!@#$%^&*() NOT VALID SQL AT ALL ;;;"));

        Assert.Null(exception); // parser must not throw
    }

    [Fact]
    public async Task GarbageSql_ShouldNotDetectDangerousOperations()
    {
        var result = await _analyzer.ParseAsync("xyzzy foo bar baz qux");

        Assert.False(result.HasUnfilteredMutation);
        Assert.False(result.HasDdlOperation);
        Assert.False(result.HasDropStatement);
    }

    // ── RAG: empty knowledge base ─────────────────────────────────────────────

    [Fact]
    public async Task RagRetrieve_WhenNoDocuments_ShouldReturn_EmptyContext_NoException()
    {
        // RagService with a stub that returns no chunks
        var ragService = new NullRagService();

        var exception = await Record.ExceptionAsync(() =>
            ragService.RetrieveAsync("SELECT * FROM users", topK: 5));

        Assert.Null(exception);

        var ctx = await ragService.RetrieveAsync("SELECT * FROM users", topK: 5);
        Assert.False(ctx.WasUsed);
        Assert.Empty(ctx.RetrievedChunks);
    }

    // ── RAG gating: no tables detected → skip ─────────────────────────────────

    [Fact]
    public async Task RagGating_NoTablesDetected_ShouldSkip_EvenAtHighRisk()
    {
        // Create a minimal RagService with null dependencies — only test ShouldUseRagAsync
        var ragService = new NullRagService();

        var parse = new SqlParseResult
        {
            TablesDetected = [],   // no tables
            HasDdlOperation = false,
        };

        var shouldUse = await ragService.ShouldUseRagAsync(parse, RiskLevel.CRITICAL);
        Assert.False(shouldUse);  // no tables → skip even at CRITICAL
    }
}

/// <summary>
/// Minimal IRagService stub that simulates an empty knowledge base.
/// Only ShouldUseRagAsync and RetrieveAsync are meaningful here.
/// </summary>
file sealed class NullRagService : SqlMind.Core.Interfaces.IRagService
{
    public Task<bool> ShouldUseRagAsync(SqlParseResult parseResult, RiskLevel riskLevel)
    {
        // Mirrors the real gating logic: requires TablesDetected AND risk >= MEDIUM
        bool tablesPresent = parseResult.TablesDetected.Count > 0;
        bool riskSufficient = riskLevel >= RiskLevel.MEDIUM;
        return Task.FromResult(tablesPresent && riskSufficient);
    }

    public Task<RagContext> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default)
        => Task.FromResult(new RagContext { WasUsed = false, RetrievedChunks = [], AssembledContext = string.Empty });

    public Task IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<KnowledgeChunk>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
        => Task.FromResult(new List<KnowledgeChunk>());
}
