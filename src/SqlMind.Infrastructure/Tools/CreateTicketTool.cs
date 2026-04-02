using Microsoft.Extensions.Logging;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Tools;

/// <summary>
/// Creates an incident ticket for high-risk SQL operations.
/// Mock implementation: logs the ticket and returns a synthetic ticket_id.
/// Replace with real Jira/ServiceNow client behind this interface when ready.
/// </summary>
public sealed class CreateTicketTool : ITool
{
    private readonly ILogger<CreateTicketTool> _logger;

    public string Name        => "create_ticket";
    public string Description => "Creates an incident ticket for a risky SQL operation.";

    public CreateTicketTool(ILogger<CreateTicketTool> logger) => _logger = logger;

    public Task<ToolExecutionResult> ExecuteAsync(
        Dictionary<string, object> input,
        CancellationToken ct = default)
    {
        var title      = GetString(input, "title");
        var priority   = GetString(input, "priority");
        var riskLevel  = GetString(input, "risk_level");
        var sqlContent = GetString(input, "sql_content");

        var ticketId = $"TICKET-{Guid.NewGuid():N[..8]}";

        _logger.LogWarning(
            "[MOCK] CreateTicket — TicketId={TicketId} Priority={Priority} RiskLevel={RiskLevel} Title={Title} SQL={Sql}",
            ticketId, priority, riskLevel, title, sqlContent.Length > 120 ? sqlContent[..120] + "…" : sqlContent);

        var output = new Dictionary<string, object>
        {
            ["ticket_id"] = ticketId,
            ["status"]    = "created",
            ["priority"]  = priority
        };

        return Task.FromResult(new ToolExecutionResult
        {
            ToolName  = Name,
            Success   = true,
            Output    = output,
            ExecutedAt = DateTimeOffset.UtcNow
        });
    }

    private static string GetString(Dictionary<string, object> input, string key)
        => input.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
}
