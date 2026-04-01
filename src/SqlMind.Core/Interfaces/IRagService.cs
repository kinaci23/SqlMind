namespace SqlMind.Core.Interfaces;

/// <summary>
/// Retrieval-Augmented Generation service.
/// Combines embedding lookup, pgvector similarity search, and context assembly.
/// MUST NOT be called unless gating conditions are met:
///   tables_detected == true AND (risk_level >= MEDIUM OR context_needed == true).
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Retrieves relevant context chunks for the given query from the knowledge base.
    /// </summary>
    /// <param name="query">The search query (typically the SQL or a summary of it).</param>
    /// <param name="topK">Maximum number of context chunks to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assembled context string ready to be injected into the LLM prompt.</returns>
    Task<string> RetrieveContextAsync(string query, int topK = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new document to the knowledge base, chunking and embedding it.
    /// </summary>
    /// <param name="content">Document content.</param>
    /// <param name="metadata">Optional metadata (source, tags, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ID of the created knowledge document.</returns>
    Task<Guid> IndexDocumentAsync(string content, IDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
}
