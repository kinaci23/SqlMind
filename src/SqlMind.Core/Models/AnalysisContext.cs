using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Runtime context passed through the agent pipeline (PolicyEngine → ToolExecutor → Tools).
/// Provides all data tools need to construct meaningful payloads without coupling them
/// to upstream pipeline stages.
/// </summary>
public sealed class AnalysisContext
{
    /// <summary>Mandatory correlation ID propagated to every audit log entry.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Original SQL script being analyzed.</summary>
    public string SqlContent { get; init; } = string.Empty;

    /// <summary>Aggregate risk level determined by the rule-based + LLM pipeline.</summary>
    public RiskLevel RiskLevel { get; init; }

    /// <summary>Detailed risk findings from IRiskEvaluator.</summary>
    public IReadOnlyList<RiskFinding> Findings { get; init; } = [];

    /// <summary>Parsed SQL structure (optional — may be null in lightweight paths).</summary>
    public SqlParseResult? ParseResult { get; init; }
}
