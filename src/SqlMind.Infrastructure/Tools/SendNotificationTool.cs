using Microsoft.Extensions.Logging;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Tools;

/// <summary>
/// Sends an alert notification for critical SQL risk events.
/// Mock implementation: logs the notification payload.
/// Replace with real Slack/Teams/email client when ready.
/// </summary>
public sealed class SendNotificationTool : ITool
{
    private readonly ILogger<SendNotificationTool> _logger;

    public string Name        => "send_notification";
    public string Description => "Sends an alert notification for a risky SQL operation.";

    public SendNotificationTool(ILogger<SendNotificationTool> logger) => _logger = logger;

    public Task<ToolExecutionResult> ExecuteAsync(
        Dictionary<string, object> input,
        CancellationToken ct = default)
    {
        var message   = GetString(input, "message");
        var channel   = GetString(input, "channel");
        var riskLevel = GetString(input, "risk_level");

        var notificationId = $"NOTIF-{Guid.NewGuid():N[..8]}";

        _logger.LogWarning(
            "[MOCK] SendNotification — NotificationId={NotifId} Channel={Channel} RiskLevel={RiskLevel} Message={Message}",
            notificationId, channel, riskLevel, message);

        var output = new Dictionary<string, object>
        {
            ["notification_id"] = notificationId,
            ["status"]          = "sent",
            ["channel"]         = channel
        };

        return Task.FromResult(new ToolExecutionResult
        {
            ToolName   = Name,
            Success    = true,
            Output     = output,
            ExecutedAt = DateTimeOffset.UtcNow
        });
    }

    private static string GetString(Dictionary<string, object> input, string key)
        => input.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
}
