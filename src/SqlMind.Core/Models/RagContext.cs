namespace SqlMind.Core.Models;

/// <summary>
/// The assembled output of the RAG retrieval step.
/// Passed downstream to the LLM prompt builder when <see cref="WasUsed"/> is true.
/// </summary>
public sealed class RagContext
{
    /// <summary>Ordered list of the top-k retrieved chunks (highest similarity first).</summary>
    public IReadOnlyList<KnowledgeChunk> RetrievedChunks { get; init; } = [];

    /// <summary>
    /// Cosine similarity scores corresponding 1-to-1 with <see cref="RetrievedChunks"/>.
    /// Values are in [0, 1]; 1.0 = identical.
    /// </summary>
    public IReadOnlyList<float> Scores { get; init; } = [];

    /// <summary>
    /// True when the gating conditions were met and retrieval ran.
    /// False when RAG was intentionally skipped (gating logic).
    /// </summary>
    public bool WasUsed { get; init; }

    /// <summary>
    /// Convenience: concatenated chunk text ready to be injected into the LLM prompt.
    /// Empty string when <see cref="WasUsed"/> is false.
    /// </summary>
    public string AssembledContext { get; init; } = string.Empty;

    /// <summary>Returns an empty, unused RAG context (gating skipped).</summary>
    public static RagContext Empty() => new() { WasUsed = false };
}
