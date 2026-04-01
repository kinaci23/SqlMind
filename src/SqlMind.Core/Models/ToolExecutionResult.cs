namespace SqlMind.Core.Models;

/// <summary>
/// Result of a single tool execution, recorded to tool_executions and audit_logs.
/// </summary>
public sealed class ToolExecutionResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ToolName { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
}
