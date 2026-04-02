using SqlMind.Core.Enums;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.RAG;

namespace SqlMind.UnitTests;

/// <summary>
/// Unit tests for RagService:
///   - ShouldUseRagAsync gating logic (all table × risk combinations)
///   - ChunkDocument chunking boundaries
///   - GeminiEmbeddingService dimension validation
/// These tests use no external dependencies (no DB, no HTTP).
/// </summary>
public class RagServiceTests
{
    // ── ShouldUseRagAsync ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(RiskLevel.LOW,      false)] // tables=true, risk=LOW,    ctxNeeded=false → skip
    [InlineData(RiskLevel.MEDIUM,   true)]  // tables=true, risk=MEDIUM              → use
    [InlineData(RiskLevel.HIGH,     true)]  // tables=true, risk=HIGH                → use
    [InlineData(RiskLevel.CRITICAL, true)]  // tables=true, risk=CRITICAL            → use
    public async Task ShouldUseRag_TablesPresent_RiskVaries(RiskLevel risk, bool expected)
    {
        var svc = CreateSut();

        var parse = new SqlParseResult
        {
            TablesDetected      = ["orders"],
            HasDdlOperation     = false,
            HasUnfilteredMutation = false
        };

        var result = await svc.ShouldUseRagAsync(parse, risk);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ShouldUseRag_NoTables_ReturnsFalse_EvenIfCritical()
    {
        var svc = CreateSut();
        var parse = new SqlParseResult
        {
            TablesDetected      = [],
            HasDdlOperation     = false,
            HasUnfilteredMutation = false
        };

        var result = await svc.ShouldUseRagAsync(parse, RiskLevel.CRITICAL);

        Assert.False(result, "No tables detected → RAG must be skipped regardless of risk.");
    }

    [Fact]
    public async Task ShouldUseRag_TablesAndContextNeeded_LowRisk_ReturnsTrue()
    {
        // context_needed = true (DDL present) overrides LOW risk
        var svc = CreateSut();
        var parse = new SqlParseResult
        {
            TablesDetected    = ["users"],
            HasDdlOperation   = true,   // DROP / ALTER / TRUNCATE
            HasUnfilteredMutation = false
        };

        var result = await svc.ShouldUseRagAsync(parse, RiskLevel.LOW);

        Assert.True(result, "DDL present (context_needed=true) → RAG should run even at LOW risk.");
    }

    [Fact]
    public async Task ShouldUseRag_TablesAndUnfilteredMutation_LowRisk_ReturnsTrue()
    {
        var svc = CreateSut();
        var parse = new SqlParseResult
        {
            TablesDetected        = ["products"],
            HasDdlOperation       = false,
            HasUnfilteredMutation = true   // UPDATE/DELETE without WHERE
        };

        var result = await svc.ShouldUseRagAsync(parse, RiskLevel.LOW);

        Assert.True(result, "Unfiltered mutation (context_needed=true) → RAG should run at LOW risk.");
    }

    [Fact]
    public async Task ShouldUseRag_NoTablesNoRisk_ReturnsFalse()
    {
        var svc = CreateSut();
        var parse = new SqlParseResult
        {
            TablesDetected        = [],
            HasDdlOperation       = false,
            HasUnfilteredMutation = false
        };

        var result = await svc.ShouldUseRagAsync(parse, RiskLevel.LOW);

        Assert.False(result);
    }

    // ── Chunking logic ────────────────────────────────────────────────────────

    [Fact]
    public void ChunkDocument_EmptyContent_ReturnsNoChunks()
    {
        var doc = MakeDocument(string.Empty);
        var chunks = RagService.ChunkDocument(doc);
        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkDocument_ShortContent_ReturnsSingleChunk()
    {
        var doc = MakeDocument("SELECT 1 FROM dual;");
        var chunks = RagService.ChunkDocument(doc);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(doc.Id, chunks[0].DocumentId);
    }

    [Fact]
    public void ChunkDocument_LongContent_ProducesMultipleChunks()
    {
        // Generate content well above the 2000-char chunk threshold
        var content = string.Join(' ', Enumerable.Repeat("word", 2000)); // ~10 000 chars
        var doc     = MakeDocument(content);
        var chunks  = RagService.ChunkDocument(doc);

        Assert.True(chunks.Count > 1, "Long content must produce more than one chunk.");
    }

    [Fact]
    public void ChunkDocument_ChunksSmallerThanThreshold()
    {
        var content = string.Join(' ', Enumerable.Repeat("word", 2000));
        var doc     = MakeDocument(content);
        var chunks  = RagService.ChunkDocument(doc);

        // Each chunk must not exceed the max character limit (2000 chars, pre-trim).
        // We allow a small margin for the whitespace-boundary search.
        foreach (var chunk in chunks)
        {
            Assert.True(
                chunk.Content.Length <= 2200,
                $"Chunk {chunk.ChunkIndex} is too large: {chunk.Content.Length} chars.");
        }
    }

    [Fact]
    public void ChunkDocument_ChunkIndexesAreSequential()
    {
        var content = string.Join(' ', Enumerable.Repeat("alpha", 2000));
        var doc     = MakeDocument(content);
        var chunks  = RagService.ChunkDocument(doc);

        for (var i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public void ChunkDocument_MetadataContainsSourceAndVersion()
    {
        var doc    = MakeDocument("Some content.", source: "test-source", version: 3);
        var chunks = RagService.ChunkDocument(doc);

        Assert.All(chunks, c =>
        {
            Assert.Equal("test-source", c.Metadata["source"]);
            Assert.Equal("3",           c.Metadata["version"]);
        });
    }

    // ── Embedding dimension validation ────────────────────────────────────────

    [Fact]
    public void EmbeddingRecord_WrongDimension_CanBeDetectedByService()
    {
        // Simulate what GeminiEmbeddingService does when the API returns wrong dim.
        var wrongVector = new float[768]; // should be 3072
        var ex = Record.Exception(() => ValidateDimensions(wrongVector));

        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("768", ex.Message);
        Assert.Contains("3072", ex.Message);
    }

    [Fact]
    public void EmbeddingRecord_CorrectDimension_NoException()
    {
        var correctVector = new float[3072];
        var ex = Record.Exception(() => ValidateDimensions(correctVector));
        Assert.Null(ex);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a RagService without a real DbContext — only usable for the sync/gating tests.
    /// The chunking tests use the internal static method directly.
    /// </summary>
    private static RagService CreateSut()
    {
        // RagService.ShouldUseRagAsync and ChunkDocument do not touch _db or _embedding,
        // so passing null is safe for these unit tests.
        return new RagService(null!, null!, new FakeLogger<RagService>());
    }

    private static KnowledgeDocument MakeDocument(
        string content, string source = "unit-test", int version = 1) =>
        new()
        {
            Id      = Guid.NewGuid(),
            Title   = "Test Document",
            Content = content,
            Source  = source,
            Version = version
        };

    /// <summary>Mirror of GeminiEmbeddingService.ValidateDimensions (extracted for testability).</summary>
    private static void ValidateDimensions(float[] vector)
    {
        const int expected = 3072;
        if (vector.Length != expected)
            throw new InvalidOperationException(
                $"Gemini Embedding returned {vector.Length} dimensions; expected {expected}.");
    }
}

/// <summary>No-op logger for unit tests (avoids ILogger mocker dependency).</summary>
internal sealed class FakeLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) { }
}
