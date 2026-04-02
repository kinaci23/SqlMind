namespace SqlMind.Core.Models;

/// <summary>
/// A document stored in the knowledge base.
/// Documents are chunked and embedded for RAG retrieval.
/// </summary>
public sealed class KnowledgeDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Human-readable title for the document.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Full raw content of the document.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Origin of the document (file path, URL, system name, etc.).</summary>
    public string Source { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Schema/content version. Increment when content is re-indexed.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Navigation — populated by EF Core or RagService after indexing.</summary>
    public IReadOnlyList<KnowledgeChunk> Chunks { get; init; } = [];
}
