using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Final output of the full analysis pipeline for a single SQL script.
/// Stored in analysis_results and linked to audit_logs via CorrelationId.
/// </summary>
public sealed class AnalysisResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Structured JSON output from the LLM (parsed from ILLMClient response).</summary>
    public string LlmOutput { get; set; } = string.Empty;

    public RiskLevel AggregateRiskLevel { get; set; }
    public IReadOnlyList<RiskFinding> Findings { get; set; } = [];

    /// <summary>Whether RAG was triggered for this analysis (per gating logic).</summary>
    public bool RagUsed { get; set; }

    /// <summary>Tool names executed as a result of policy evaluation.</summary>
    public IReadOnlyList<string> ExecutedTools { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
