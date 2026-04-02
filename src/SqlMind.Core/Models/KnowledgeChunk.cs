namespace SqlMind.Core.Models;

/// <summary>
/// A chunk of a <see cref="KnowledgeDocument"/> that is individually embedded and stored in pgvector.
/// Chunking uses a sliding window: ~500 tokens per chunk, 50-token overlap.
/// </summary>
public sealed class KnowledgeChunk
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>FK to the parent document.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Text content of this chunk.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Zero-based position of this chunk within the document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Arbitrary metadata (e.g. dialect, environment, section header).</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>Navigation — populated by EF Core.</summary>
    public KnowledgeDocument? Document { get; init; }

    /// <summary>Navigation — embedding for this chunk, if indexed.</summary>
    public EmbeddingRecord? Embedding { get; init; }
}
