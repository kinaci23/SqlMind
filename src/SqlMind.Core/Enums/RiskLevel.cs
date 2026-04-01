namespace SqlMind.Core.Enums;

/// <summary>
/// Risk severity levels. Rule-based engine is PRIMARY — a CRITICAL result cannot be
/// downgraded by the LLM layer.
/// </summary>
public enum RiskLevel
{
    LOW = 0,
    MEDIUM = 1,
    HIGH = 2,
    CRITICAL = 3
}
