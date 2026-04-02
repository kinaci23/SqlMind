using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Configurable mapping of RiskLevel → approved ActionTypes.
/// Loaded from appsettings.json (section "PolicyConfig:Rules").
/// Hard-coded IF/switch logic is FORBIDDEN — all decisions derive from this config.
/// </summary>
public sealed class PolicyConfig
{
    /// <summary>
    /// Key: RiskLevel name (e.g. "CRITICAL", "HIGH").
    /// Value: ordered list of ActionType names to execute.
    /// </summary>
    public Dictionary<string, List<string>> Rules { get; set; } = new();

    /// <summary>Returns default policy rules used when config is absent.</summary>
    public static PolicyConfig Default() => new()
    {
        Rules = new Dictionary<string, List<string>>
        {
            ["CRITICAL"] = ["CreateTicket", "SendNotification", "RequestApproval"],
            ["HIGH"]     = ["CreateTicket"],
            ["MEDIUM"]   = ["WarnLog"],
            ["LOW"]      = ["LogOnly"]
        }
    };

    /// <summary>Resolves the ActionType list for the given risk level, falling back to LogOnly.</summary>
    public IReadOnlyList<ActionType> GetActions(RiskLevel riskLevel)
    {
        var key = riskLevel.ToString();
        if (!Rules.TryGetValue(key, out var names))
            return [ActionType.LogOnly];

        var result = new List<ActionType>(names.Count);
        foreach (var name in names)
        {
            if (Enum.TryParse<ActionType>(name, ignoreCase: true, out var action))
                result.Add(action);
        }
        return result;
    }
}
