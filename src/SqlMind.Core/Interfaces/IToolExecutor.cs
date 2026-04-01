using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Orchestrates tool execution after IPolicyEngine approval.
/// Resolves the correct ITool implementation, passes inputs, and records results to audit_logs.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a named tool with the provided input and records the execution.
    /// </summary>
    /// <param name="toolName">Tool name matching ITool.Name.</param>
    /// <param name="input">Input payload for the tool.</param>
    /// <param name="correlationId">Audit correlation ID — mandatory for all executions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result containing output and status.</returns>
    Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        object input,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all registered tool names available for execution.
    /// </summary>
    IReadOnlyList<string> GetAvailableTools();
}
