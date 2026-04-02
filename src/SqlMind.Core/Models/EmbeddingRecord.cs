namespace SqlMind.Core.Models;

/// <summary>
/// Stores the dense vector embedding for a <see cref="KnowledgeChunk"/>.
/// The underlying DB column is vector(3072) in PostgreSQL (Gemini gemini-embedding-001 size).
/// The float[] is mapped to the pgvector column type by the DbContext value converter.
/// </summary>
public sealed class EmbeddingRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>FK to the chunk this embedding belongs to.</summary>
    public Guid ChunkId { get; init; }

    /// <summary>
    /// The 768-dimensional embedding as a raw float array.
    /// Stored in PostgreSQL as vector(768) via a value converter in SqlMindDbContext.
    /// </summary>
    public float[] Vector { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation — populated by EF Core.</summary>
    public KnowledgeChunk? Chunk { get; init; }
}
