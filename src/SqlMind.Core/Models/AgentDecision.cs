namespace SqlMind.Core.Models;

/// <summary>
/// Represents a single step in the agent's ReAct loop (Observe / Think / Act).
/// Every decision is logged for audit purposes.
/// </summary>
public sealed class AgentDecision
{
    public DecisionType DecisionType { get; init; }

    /// <summary>Tool name involved in an Act step; null for Observe / Think steps.</summary>
    public string? ToolName { get; init; }

    /// <summary>Input payload passed to the tool; null for non-Act steps.</summary>
    public Dictionary<string, object>? ToolInput { get; init; }

    /// <summary>Human-readable explanation of why this decision was made.</summary>
    public string Reasoning { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Phase of the ReAct loop.</summary>
public enum DecisionType
{
    Observe,
    Think,
    Act
}
