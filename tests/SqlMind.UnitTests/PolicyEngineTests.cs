using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Core.Enums;
using SqlMind.Infrastructure.Policy;

namespace SqlMind.UnitTests;

/// <summary>
/// Unit tests for PolicyEngine — verifies that each RiskLevel maps to the correct
/// ActionType list as defined in configuration, with no hard-coded IF logic.
/// </summary>
public sealed class PolicyEngineTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a PolicyEngine backed by the given in-memory config rules.</summary>
    private static PolicyEngine BuildEngine(Dictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new PolicyEngine(config, NullLogger<PolicyEngine>.Instance);
    }

    private static PolicyEngine BuildDefaultEngine() => BuildEngine(new Dictionary<string, string?>
    {
        ["PolicyConfig:Rules:CRITICAL:0"] = "CreateTicket",
        ["PolicyConfig:Rules:CRITICAL:1"] = "SendNotification",
        ["PolicyConfig:Rules:CRITICAL:2"] = "RequestApproval",
        ["PolicyConfig:Rules:HIGH:0"]     = "CreateTicket",
        ["PolicyConfig:Rules:MEDIUM:0"]   = "WarnLog",
        ["PolicyConfig:Rules:LOW:0"]      = "LogOnly"
    });

    // ── EvaluateAsync — correct action lists ─────────────────────────────────

    [Fact]
    public async Task Critical_ShouldReturn_CreateTicket_SendNotification_RequestApproval()
    {
        var engine  = BuildDefaultEngine();
        var actions = await engine.EvaluateAsync(RiskLevel.CRITICAL);

        Assert.Equal(3, actions.Count);
        Assert.Contains(ActionType.CreateTicket,     actions);
        Assert.Contains(ActionType.SendNotification, actions);
        Assert.Contains(ActionType.RequestApproval,  actions);
    }

    [Fact]
    public async Task High_ShouldReturn_CreateTicket_Only()
    {
        var engine  = BuildDefaultEngine();
        var actions = await engine.EvaluateAsync(RiskLevel.HIGH);

        Assert.Single(actions);
        Assert.Equal(ActionType.CreateTicket, actions[0]);
    }

    [Fact]
    public async Task Medium_ShouldReturn_WarnLog_Only()
    {
        var engine  = BuildDefaultEngine();
        var actions = await engine.EvaluateAsync(RiskLevel.MEDIUM);

        Assert.Single(actions);
        Assert.Equal(ActionType.WarnLog, actions[0]);
    }

    [Fact]
    public async Task Low_ShouldReturn_LogOnly_Only()
    {
        var engine  = BuildDefaultEngine();
        var actions = await engine.EvaluateAsync(RiskLevel.LOW);

        Assert.Single(actions);
        Assert.Equal(ActionType.LogOnly, actions[0]);
    }

    // ── GetConfigAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfigAsync_ShouldReturn_PolicyConfig_With_AllRules()
    {
        var engine = BuildDefaultEngine();
        var config = await engine.GetConfigAsync();

        Assert.NotNull(config);
        Assert.True(config.Rules.ContainsKey("CRITICAL"));
        Assert.True(config.Rules.ContainsKey("HIGH"));
        Assert.True(config.Rules.ContainsKey("MEDIUM"));
        Assert.True(config.Rules.ContainsKey("LOW"));
    }

    // ── Missing config → default fallback ────────────────────────────────────

    [Fact]
    public async Task MissingConfig_ShouldFallBackToDefaults()
    {
        var engine  = BuildEngine(new Dictionary<string, string?>()); // empty config
        var actions = await engine.EvaluateAsync(RiskLevel.CRITICAL);

        // Default config has CRITICAL → 3 actions
        Assert.Equal(3, actions.Count);
        Assert.Contains(ActionType.CreateTicket,     actions);
        Assert.Contains(ActionType.SendNotification, actions);
        Assert.Contains(ActionType.RequestApproval,  actions);
    }

    [Fact]
    public async Task MissingRiskLevelKey_ShouldFallBackToLogOnly()
    {
        // Config with only CRITICAL defined — querying HIGH should return LogOnly
        var engine  = BuildEngine(new Dictionary<string, string?>
        {
            ["PolicyConfig:Rules:CRITICAL:0"] = "CreateTicket"
        });
        var actions = await engine.EvaluateAsync(RiskLevel.HIGH);

        Assert.Single(actions);
        Assert.Equal(ActionType.LogOnly, actions[0]);
    }
}
