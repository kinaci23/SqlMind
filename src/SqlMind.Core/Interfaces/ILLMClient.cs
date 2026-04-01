namespace SqlMind.Core.Interfaces;

/// <summary>
/// LLM provider abstraction. All LLM calls must go through this interface.
/// Temperature must be kept between 0.0–0.2 for deterministic output.
/// Output is always structured JSON — free text responses are forbidden.
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Sends a prompt to the LLM and returns a structured JSON response.
    /// </summary>
    /// <param name="systemPrompt">Isolated system prompt to prevent injection.</param>
    /// <param name="userPrompt">User-supplied prompt, sanitized before passing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw JSON string conforming to the LLM output schema.</returns>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider name (e.g., "gemini", "openai", "anthropic").
    /// </summary>
    string ProviderName { get; }
}
