using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Policy;

/// <summary>
/// Policy engine that maps RiskLevel → List&lt;ActionType&gt; using rules loaded from
/// IConfiguration (section "PolicyConfig:Rules").  Hard-coded IF/switch is FORBIDDEN —
/// all routing goes through the config-driven PolicyConfig model.
/// </summary>
public sealed class PolicyEngine : IPolicyEngine
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PolicyEngine> _logger;
    private PolicyConfig _config;

    public PolicyEngine(IConfiguration configuration, ILogger<PolicyEngine> logger)
    {
        _configuration = configuration;
        _logger        = logger;
        _config        = LoadConfig();
    }

    public Task<List<ActionType>> EvaluateAsync(RiskLevel riskLevel, CancellationToken ct = default)
    {
        var actions = _config.GetActions(riskLevel);

        _logger.LogInformation(
            "PolicyEngine evaluated RiskLevel={RiskLevel} → Actions=[{Actions}]",
            riskLevel,
            string.Join(", ", actions));

        return Task.FromResult(actions.ToList());
    }

    public Task<PolicyConfig> GetConfigAsync() => Task.FromResult(_config);

    // ── private ──────────────────────────────────────────────────────────────

    private PolicyConfig LoadConfig()
    {
        var section = _configuration.GetSection("PolicyConfig:Rules");
        if (!section.Exists())
        {
            _logger.LogWarning("PolicyConfig:Rules not found in configuration — using defaults.");
            return PolicyConfig.Default();
        }

        var config = new PolicyConfig();
        foreach (var child in section.GetChildren())
        {
            var names = child.GetChildren().Select(c => c.Value ?? string.Empty).ToList();
            config.Rules[child.Key] = names;
        }

        _logger.LogInformation("PolicyConfig loaded with {Count} risk-level rules.", config.Rules.Count);
        return config;
    }
}
