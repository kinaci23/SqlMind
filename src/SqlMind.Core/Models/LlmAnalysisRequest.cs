using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Input to the LLM analysis layer. Carries the parse result, current rule-based
/// risk level, and optional RAG context assembled by IRagService.
/// </summary>
public sealed class LlmAnalysisRequest
{
    /// <summary>Structured output from ISqlAnalyzer — the primary input.</summary>
    public required SqlParseResult ParseResult { get; init; }

    /// <summary>Risk level already determined by the rule-based engine (PRIMARY layer).</summary>
    public required RiskLevel RuleBasedRiskLevel { get; init; }

    /// <summary>
    /// Optional context documents retrieved by IRagService.
    /// Null or empty when RAG gating logic decided to skip retrieval.
    /// </summary>
    public string? RagContext { get; init; }

    /// <summary>Correlation ID propagated from the originating API request.</summary>
    public required string CorrelationId { get; init; }
}
