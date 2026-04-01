using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Risk;

/// <summary>
/// PRIMARY risk evaluation layer. Rules are stored as data — no hard-coded if/else chains.
/// Rule-based CRITICAL findings cannot be downgraded by the LLM layer.
/// </summary>
public sealed class RuleBasedRiskEvaluator : IRiskEvaluator
{
    // -------------------------------------------------------------------------
    // Rule definition (data-driven, not hard-coded IFs)
    // -------------------------------------------------------------------------

    private sealed class RiskRule
    {
        public required string RuleId        { get; init; }
        public required RiskLevel Level      { get; init; }
        public required string Description   { get; init; }
        public required Func<SqlParseResult, bool> Matches { get; init; }
    }

    private static readonly IReadOnlyList<RiskRule> Rules =
    [
        // ── CRITICAL ──────────────────────────────────────────────────────────
        new RiskRule
        {
            RuleId      = "RULE-C001",
            Level       = RiskLevel.CRITICAL,
            Description = "DELETE statement without WHERE clause — risk of full-table deletion",
            Matches     = r => r.Operations.Contains(OperationType.DELETE) && r.HasUnfilteredMutation
        },
        new RiskRule
        {
            RuleId      = "RULE-C002",
            Level       = RiskLevel.CRITICAL,
            Description = "DROP TABLE/VIEW/INDEX detected — irreversible structural change",
            Matches     = r => r.HasDropStatement
        },
        new RiskRule
        {
            RuleId      = "RULE-C003",
            Level       = RiskLevel.CRITICAL,
            Description = "TRUNCATE detected — removes all rows without transactional safety in some engines",
            Matches     = r => r.HasTruncateStatement
        },

        // ── HIGH ──────────────────────────────────────────────────────────────
        new RiskRule
        {
            RuleId      = "RULE-H001",
            Level       = RiskLevel.HIGH,
            Description = "UPDATE statement without WHERE clause — risk of full-table modification",
            Matches     = r => r.Operations.Contains(OperationType.UPDATE) && r.HasUnfilteredMutation
        },
        new RiskRule
        {
            RuleId      = "RULE-H002",
            Level       = RiskLevel.HIGH,
            Description = "ALTER TABLE detected — schema change may cause downtime or data loss",
            Matches     = r => r.HasAlterStatement
        },
        new RiskRule
        {
            RuleId      = "RULE-H003",
            Level       = RiskLevel.HIGH,
            Description = "Filtered DELETE (WHERE present) — modifies data; verify scope before execution",
            Matches     = r => r.Operations.Contains(OperationType.DELETE)
                               && !r.HasUnfilteredMutation
                               && r.WhereClauseExists
        },

        // ── MEDIUM ────────────────────────────────────────────────────────────
        new RiskRule
        {
            RuleId      = "RULE-M001",
            Level       = RiskLevel.MEDIUM,
            Description = "JOIN operation detected — potential for cross-table data exposure or performance impact",
            Matches     = r => r.JoinExists
        },

        // ── LOW (catch-all) ───────────────────────────────────────────────────
        new RiskRule
        {
            RuleId      = "RULE-L001",
            Level       = RiskLevel.LOW,
            Description = "Standard SQL operation — no elevated risk patterns detected",
            Matches     = r => !r.HasUnfilteredMutation
                               && !r.HasDdlOperation
                               && !r.JoinExists
                               && !r.Operations.Contains(OperationType.DELETE)
        }
    ];

    // ─── Score mapping (centre of each band) ────────────────────────────────
    private static float ScoreFor(RiskLevel level) => level switch
    {
        RiskLevel.CRITICAL => 0.95f,
        RiskLevel.HIGH     => 0.75f,
        RiskLevel.MEDIUM   => 0.45f,
        _                  => 0.10f
    };

    // -------------------------------------------------------------------------
    // IRiskEvaluator implementation
    // -------------------------------------------------------------------------

    public Task<IReadOnlyList<RiskFinding>> EvaluateAsync(
        SqlParseResult parseResult,
        string? llmInsights,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<RiskFinding>();

        foreach (var rule in Rules)
        {
            if (!rule.Matches(parseResult)) continue;

            findings.Add(new RiskFinding
            {
                RuleId            = rule.RuleId,
                Level             = rule.Level,
                Description       = rule.Description,
                IsPrimary         = true,
                Score             = ScoreFor(rule.Level),
                AffectedOperation = parseResult.Operations.Count > 0
                                        ? parseResult.Operations[0].ToString()
                                        : null,
                AffectedTable     = parseResult.TablesDetected.Count > 0
                                        ? parseResult.TablesDetected[0]
                                        : null
            });
        }

        // Guarantee at least one finding
        if (findings.Count == 0)
        {
            findings.Add(new RiskFinding
            {
                RuleId      = "RULE-L001",
                Level       = RiskLevel.LOW,
                Description = "Standard SQL operation — no elevated risk patterns detected",
                IsPrimary   = true,
                Score       = 0.10f
            });
        }

        // Sort descending by level so callers see the most severe finding first
        findings.Sort((a, b) => b.Level.CompareTo(a.Level));

        return Task.FromResult<IReadOnlyList<RiskFinding>>(findings.AsReadOnly());
    }

    public RiskLevel GetAggregateLevel(IReadOnlyList<RiskFinding> findings)
    {
        if (findings.Count == 0) return RiskLevel.LOW;
        return findings.Max(f => f.Level);
    }
}
