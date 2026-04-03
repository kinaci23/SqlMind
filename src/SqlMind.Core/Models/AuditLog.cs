namespace SqlMind.Core.Models;

/// <summary>
/// Immutable audit record written at the end of every analysis pipeline run.
/// Maps to the audit_logs table. All fields are mandatory per CLAUDE.md.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Propagated across every pipeline step and tool execution.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>SHA-256 hex of the original SQL — enables cache-hit detection.</summary>
    public string InputHash { get; init; } = string.Empty;

    /// <summary>Serialised SqlParseResult (JSON).</summary>
    public string SqlParseResult { get; init; } = string.Empty;

    /// <summary>Serialised list of rule IDs that fired (JSON array of strings).</summary>
    public string RuleTriggers { get; init; } = string.Empty;

    /// <summary>Raw LLM JSON output (or empty string when cache hit).</summary>
    public string LlmOutput { get; init; } = string.Empty;

    /// <summary>Whether the RAG pipeline was invoked for this request.</summary>
    public bool RagUsed { get; init; }

    /// <summary>Serialised list of ToolExecutionResult objects (JSON).</summary>
    public string ToolExecution { get; init; } = string.Empty;

    /// <summary>ISO 8601 timestamp of when the audit record was written.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
