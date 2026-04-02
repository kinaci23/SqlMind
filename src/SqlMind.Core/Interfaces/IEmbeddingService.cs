namespace SqlMind.Core.Interfaces;

/// <summary>
/// Converts text to dense vector embeddings for similarity search.
/// Default implementation uses Gemini text-embedding-004 (768 dimensions).
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Generates an embedding vector for the given text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Generates embeddings for multiple texts. Batches may be sent in a single API call.</summary>
    Task<List<float[]>> EmbedBatchAsync(List<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Dimensionality of the vectors produced by this service.
    /// Must be 768 for Gemini text-embedding-004.
    /// </summary>
    int Dimensions { get; }
}
