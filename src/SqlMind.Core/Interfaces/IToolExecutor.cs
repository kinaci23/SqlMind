using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Orchestrates tool execution after IPolicyEngine approval.
/// Resolves the correct ITool implementation per ActionType, passes inputs built from
/// AnalysisContext, and records every result for audit.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes all approved actions using the provided analysis context.
    /// </summary>
    /// <param name="actions">Ordered list of approved actions from IPolicyEngine.</param>
    /// <param name="context">Runtime context used to build each tool's input payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ActionType> actions,
        AnalysisContext context,
        CancellationToken ct = default);

    /// <summary>Returns all registered ITool implementations available for execution.</summary>
    Task<List<ITool>> GetAvailableToolsAsync();
}
