using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Retrieval-Augmented Generation service.
/// Combines embedding lookup, pgvector similarity search, and context assembly.
///
/// HARD RULE: RAG must NOT run unless gating conditions are satisfied:
///   tables_detected == true AND (risk_level >= MEDIUM OR context_needed == true)
/// Call <see cref="ShouldUseRagAsync"/> before <see cref="RetrieveAsync"/>.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Retrieves relevant context chunks for the given query via pgvector cosine similarity.
    /// </summary>
    /// <param name="query">The search query (typically the SQL or a normalised summary of it).</param>
    /// <param name="topK">Maximum number of chunks to return (default 5).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="RagContext"/> with retrieved chunks, scores, and the assembled context string.
    /// </returns>
    Task<RagContext> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default);

    /// <summary>
    /// Adds a document to the knowledge base: chunks it, embeds each chunk, persists to pgvector.
    /// </summary>
    Task IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct = default);

    /// <summary>
    /// RAG gating logic (CLAUDE.md):
    ///   tables_detected == true AND (risk_level >= MEDIUM OR context_needed == true)
    /// Returns true only when retrieval is warranted. Caller must respect the result.
    /// </summary>
    Task<bool> ShouldUseRagAsync(SqlParseResult parseResult, RiskLevel riskLevel);
}
