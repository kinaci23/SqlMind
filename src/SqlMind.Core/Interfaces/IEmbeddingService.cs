namespace SqlMind.Core.Interfaces;

/// <summary>
/// Converts text to dense vector embeddings for similarity search.
/// Default implementation uses Gemini Embedding API.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">Input text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Float array representing the embedding vector.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in a single batch call.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dimensionality of the embedding vectors produced by this service.
    /// </summary>
    int Dimensions { get; }
}
