using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Combines rule-based risk scoring (deterministic, PRIMARY) with LLM-based
/// risk insights (SECONDARY). Rule-based result takes precedence:
/// if rule-based yields CRITICAL, LLM cannot downgrade it.
/// </summary>
public interface IRiskEvaluator
{
    /// <summary>
    /// Evaluates risk for a parsed SQL result, incorporating LLM insights
    /// where applicable. Returns the final consolidated risk assessment.
    /// </summary>
    /// <param name="parseResult">Output from ISqlAnalyzer.ParseAsync.</param>
    /// <param name="llmInsights">Optional JSON string from ILLMClient (may be null if skipped).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of risk findings ordered by severity descending.</returns>
    Task<IReadOnlyList<RiskFinding>> EvaluateAsync(
        SqlParseResult parseResult,
        string? llmInsights,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the aggregate (highest) risk level from a set of findings.
    /// </summary>
    RiskLevel GetAggregateLevel(IReadOnlyList<RiskFinding> findings);
}
