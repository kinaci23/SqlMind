namespace SqlMind.Core.Interfaces;

/// <summary>
/// Represents a single executable tool (e.g., CreateTicketTool, SendNotificationTool).
/// Every tool has a declared input/output schema and executes only after
/// IPolicyEngine approval. Direct execution bypassing IToolExecutor is FORBIDDEN.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool identifier used in policy configuration (e.g., "create_ticket").</summary>
    string Name { get; }

    /// <summary>Human-readable description of what this tool does.</summary>
    string Description { get; }

    /// <summary>
    /// Executes the tool with the given input payload.
    /// </summary>
    /// <param name="input">JSON-serializable input object matching the tool's input schema.</param>
    /// <param name="correlationId">Audit correlation ID — must be propagated to audit_logs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON-serializable output object matching the tool's output schema.</returns>
    Task<object> ExecuteAsync(object input, string correlationId, CancellationToken cancellationToken = default);
}
