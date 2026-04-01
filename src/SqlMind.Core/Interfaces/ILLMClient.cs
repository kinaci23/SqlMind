using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// LLM provider abstraction. All LLM calls must go through this interface.
/// Temperature must be kept between 0.0–0.2 for deterministic output.
/// Output is always structured JSON — free text responses are forbidden.
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Sends a prompt to the LLM and returns a raw JSON string.
    /// System and user prompts are kept separate to prevent prompt injection.
    /// </summary>
    /// <param name="systemPrompt">Isolated system prompt (role, rules, output schema).</param>
    /// <param name="userPrompt">SQL content and context — sanitized before passing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw JSON string conforming to the LLM output schema.</returns>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a SQL script using the full request context and returns a validated result.
    /// Combines prompt building, LLM call, JSON parsing, and schema validation.
    /// </summary>
    Task<LlmAnalysisResult> AnalyzeAsync(
        LlmAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the LLM provider is reachable and responding.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the provider name (e.g., "gemini", "openai", "anthropic").</summary>
    string ProviderName { get; }
}
