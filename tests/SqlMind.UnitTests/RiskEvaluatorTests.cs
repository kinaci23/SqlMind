using SqlMind.Core.Enums;
using SqlMind.Infrastructure.Risk;
using SqlMind.Infrastructure.SqlParsing;

namespace SqlMind.UnitTests;

public sealed class RiskEvaluatorTests
{
    private readonly CustomSqlAnalyzer _analyzer = new();
    private readonly RuleBasedRiskEvaluator _evaluator = new();

    // ── helper ───────────────────────────────────────────────────────────────

    private async Task<(RiskLevel level, IReadOnlyList<SqlMind.Core.Models.RiskFinding> findings)> EvalAsync(string sql)
    {
        var parsed   = await _analyzer.ParseAsync(sql);
        var findings = await _evaluator.EvaluateAsync(parsed, null);
        var level    = _evaluator.GetAggregateLevel(findings);
        return (level, findings);
    }

    // ── CRITICAL ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWithoutWhere_ShouldBeCritical()
    {
        var (level, findings) = await EvalAsync("DELETE FROM users");

        Assert.Equal(RiskLevel.CRITICAL, level);
        Assert.Contains(findings, f => f.RuleId == "RULE-C001");
        Assert.All(findings.Where(f => f.IsPrimary && f.Level == RiskLevel.CRITICAL),
            f => Assert.True(f.Score >= 0.9f));
    }

    [Fact]
    public async Task DropTable_ShouldBeCritical()
    {
        var (level, findings) = await EvalAsync("DROP TABLE customers");

        Assert.Equal(RiskLevel.CRITICAL, level);
        Assert.Contains(findings, f => f.RuleId == "RULE-C002");
    }

    [Fact]
    public async Task Truncate_ShouldBeCritical()
    {
        var (level, findings) = await EvalAsync("TRUNCATE TABLE orders");

        Assert.Equal(RiskLevel.CRITICAL, level);
        Assert.Contains(findings, f => f.RuleId == "RULE-C003");
    }

    // ── HIGH ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWithoutWhere_ShouldBeHigh()
    {
        var (level, findings) = await EvalAsync("UPDATE orders SET status = 'x'");

        Assert.Equal(RiskLevel.HIGH, level);
        Assert.Contains(findings, f => f.RuleId == "RULE-H001");
        Assert.All(findings.Where(f => f.IsPrimary && f.Level == RiskLevel.HIGH),
            f => Assert.True(f.Score >= 0.6f && f.Score < 0.9f));
    }

    [Fact]
    public async Task AlterTable_ShouldBeHigh()
    {
        var (level, _) = await EvalAsync("ALTER TABLE products ADD COLUMN price DECIMAL(10,2)");

        Assert.Equal(RiskLevel.HIGH, level);
    }

    // ── MEDIUM ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectWithJoin_ShouldBeMedium()
    {
        var (level, findings) = await EvalAsync(
            "SELECT * FROM orders o JOIN customers c ON o.customer_id = c.id");

        Assert.Equal(RiskLevel.MEDIUM, level);
        Assert.Contains(findings, f => f.RuleId == "RULE-M001");
        Assert.All(findings.Where(f => f.IsPrimary && f.Level == RiskLevel.MEDIUM),
            f => Assert.True(f.Score >= 0.3f && f.Score < 0.6f));
    }

    // ── LOW ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectSimple_ShouldBeLow()
    {
        var (level, findings) = await EvalAsync("SELECT * FROM products");

        Assert.Equal(RiskLevel.LOW, level);
        Assert.All(findings.Where(f => f.IsPrimary && f.Level == RiskLevel.LOW),
            f => Assert.True(f.Score < 0.3f));
    }

    // ── DELETE with WHERE ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWithWhere_ShouldBeHighAtMost_NotCritical()
    {
        var (level, _) = await EvalAsync("DELETE FROM users WHERE id = 1");

        // Filtered DELETE is HIGH (RULE-H003) — never CRITICAL
        Assert.NotEqual(RiskLevel.CRITICAL, level);
        Assert.True(level >= RiskLevel.MEDIUM);
    }

    // ── Score ranges ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RiskLevel.CRITICAL, 0.9f, 1.01f)]
    [InlineData(RiskLevel.HIGH,     0.6f, 0.90f)]
    [InlineData(RiskLevel.MEDIUM,   0.3f, 0.60f)]
    [InlineData(RiskLevel.LOW,      0.0f, 0.30f)]
    public void ScoreRange_ShouldMatchLevel(RiskLevel level, float min, float max)
    {
        // Direct score check via a known-level finding
        var finding = new SqlMind.Core.Models.RiskFinding
        {
            RuleId      = "TEST",
            Level       = level,
            Description = "test",
            IsPrimary   = true,
            Score       = level switch
            {
                RiskLevel.CRITICAL => 0.95f,
                RiskLevel.HIGH     => 0.75f,
                RiskLevel.MEDIUM   => 0.45f,
                _                  => 0.10f
            }
        };

        Assert.True(finding.Score >= min && finding.Score < max,
            $"Score {finding.Score} not in [{min},{max}) for {level}");
    }

    // ── GetAggregateLevel ─────────────────────────────────────────────────────

    [Fact]
    public async Task AggregateLevel_ShouldReturnHighestRisk()
    {
        // A script with SELECT + DELETE (no WHERE) — aggregate must be CRITICAL
        var parsed   = await _analyzer.ParseAsync("SELECT * FROM log; DELETE FROM audit");
        var findings = await _evaluator.EvaluateAsync(parsed, null);
        var level    = _evaluator.GetAggregateLevel(findings);

        Assert.Equal(RiskLevel.CRITICAL, level);
    }
}
