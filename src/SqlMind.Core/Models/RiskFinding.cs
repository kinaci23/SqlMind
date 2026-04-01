using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// A single risk signal detected during analysis. May originate from the
/// rule-based engine (IsPrimary = true) or from LLM insights (IsPrimary = false).
/// Rule-based findings with CRITICAL level cannot be overridden.
/// </summary>
public sealed class RiskFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RiskLevel Level { get; init; }
    public string RuleId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? AffectedTable { get; init; }
    public string? AffectedOperation { get; init; }

    /// <summary>True if this finding was produced by the deterministic rule engine.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Numeric risk score: CRITICAL 0.9–1.0, HIGH 0.6–0.89, MEDIUM 0.3–0.59, LOW 0.0–0.29.</summary>
    public float Score { get; init; }
}
