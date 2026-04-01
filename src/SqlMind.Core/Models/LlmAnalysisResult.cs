namespace SqlMind.Core.Models;

/// <summary>
/// Structured output from the LLM layer. Always produced from validated JSON —
/// free-text responses are rejected. Maps 1-to-1 to the required output schema.
/// </summary>
public sealed class LlmAnalysisResult
{
    /// <summary>Non-technical summary suitable for business stakeholders.</summary>
    public required string BusinessSummary { get; init; }

    /// <summary>Detailed technical explanation of the SQL and its risk.</summary>
    public required string TechnicalSummary { get; init; }

    /// <summary>LLM-identified risk signals that complement rule-based findings.</summary>
    public required IReadOnlyList<string> RiskInsights { get; init; }

    /// <summary>Areas where the LLM lacks confidence or needs more context.</summary>
    public required IReadOnlyList<string> Uncertainties { get; init; }

    /// <summary>Concrete actions recommended before executing the SQL.</summary>
    public required IReadOnlyList<string> RecommendedActions { get; init; }

    /// <summary>Raw JSON string returned by the LLM — kept for debugging and audit.</summary>
    public required string RawJson { get; init; }
}
