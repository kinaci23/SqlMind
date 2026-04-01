using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Determines which actions to take based on risk level.
/// Policy rules are loaded from configuration or database — hard-coded IF logic is FORBIDDEN.
/// Tool execution only happens after this engine grants approval.
/// </summary>
public interface IPolicyEngine
{
    /// <summary>
    /// Evaluates the risk findings and returns the list of tool names that should be executed.
    /// </summary>
    /// <param name="riskLevel">Aggregate risk level from IRiskEvaluator.</param>
    /// <param name="findings">Detailed risk findings for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of tool names approved for execution.</returns>
    Task<IReadOnlyList<string>> GetApprovedActionsAsync(
        RiskLevel riskLevel,
        IReadOnlyList<RiskFinding> findings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads policy rules from the backing store (config or DB).
    /// </summary>
    Task ReloadPoliciesAsync(CancellationToken cancellationToken = default);
}
