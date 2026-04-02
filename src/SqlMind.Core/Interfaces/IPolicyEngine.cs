using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Determines which actions to take based on risk level.
/// Policy rules are loaded from configuration — hard-coded IF logic is FORBIDDEN.
/// Tool execution only happens after this engine grants approval.
/// </summary>
public interface IPolicyEngine
{
    /// <summary>
    /// Evaluates the risk level and returns the ordered list of approved ActionTypes.
    /// </summary>
    Task<List<ActionType>> EvaluateAsync(RiskLevel riskLevel, CancellationToken ct = default);

    /// <summary>
    /// Returns the currently active policy configuration.
    /// </summary>
    Task<PolicyConfig> GetConfigAsync();
}
