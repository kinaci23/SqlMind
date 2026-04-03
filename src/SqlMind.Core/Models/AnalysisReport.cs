using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Full JSON report returned by GET /api/v1/analyze/{job_id} once the job completes.
/// This is the API response DTO — assembled from the persisted AnalysisResult + related data.
/// </summary>
public sealed class AnalysisReport
{
    public Guid JobId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    // ── Summaries ─────────────────────────────────────────────────────────────
    public string SummaryBusiness { get; init; } = string.Empty;
    public string SummaryTechnical { get; init; } = string.Empty;

    // ── SQL parse output ──────────────────────────────────────────────────────
    public IReadOnlyList<string> Operations { get; init; } = [];
    public IReadOnlyList<string> AffectedTables { get; init; } = [];
    public IReadOnlyList<string> AffectedColumns { get; init; } = [];

    // ── Risk ──────────────────────────────────────────────────────────────────
    public string RiskLevel { get; init; } = string.Empty;
    public float RiskScore { get; init; }
    public IReadOnlyList<string> RiskReasons { get; init; } = [];
    public IReadOnlyList<string> RuleTriggers { get; init; } = [];

    // ── LLM layer ─────────────────────────────────────────────────────────────
    public IReadOnlyList<string> LlmInsights { get; init; } = [];
    public float Confidence { get; init; }

    // ── RAG & actions ─────────────────────────────────────────────────────────
    public bool RagUsed { get; init; }
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];
    public IReadOnlyList<string> ExecutedActions { get; init; } = [];

    // ── Metadata ──────────────────────────────────────────────────────────────
    public long ProcessingTimeMs { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
