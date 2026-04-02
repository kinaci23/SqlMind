using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Represents a single executable tool (e.g. CreateTicketTool, SendNotificationTool).
/// Every tool has a declared input schema and executes only after IPolicyEngine approval.
/// Direct execution bypassing IToolExecutor is FORBIDDEN.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool identifier used in policy configuration (e.g. "create_ticket").</summary>
    string Name { get; }

    /// <summary>Human-readable description of what this tool does.</summary>
    string Description { get; }

    /// <summary>
    /// Executes the tool with a schema-validated input payload.
    /// </summary>
    /// <param name="input">Key-value input matching this tool's expected schema.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ToolExecutionResult> ExecuteAsync(Dictionary<string, object> input, CancellationToken ct = default);
}
