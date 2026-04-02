using Microsoft.Extensions.Logging;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Tools;

/// <summary>
/// Requests human approval before a high-risk SQL operation proceeds.
/// Mock implementation: logs the approval request payload.
/// Replace with real approval workflow (ServiceNow, Jira approval, email) when ready.
/// </summary>
public sealed class RequestApprovalTool : ITool
{
    private readonly ILogger<RequestApprovalTool> _logger;

    public string Name        => "request_approval";
    public string Description => "Requests human approval for a critical SQL operation.";

    public RequestApprovalTool(ILogger<RequestApprovalTool> logger) => _logger = logger;

    public Task<ToolExecutionResult> ExecuteAsync(
        Dictionary<string, object> input,
        CancellationToken ct = default)
    {
        var approverEmail = GetString(input, "approver_email");
        var context       = GetString(input, "context");
        var riskLevel     = GetString(input, "risk_level");
        var sqlContent    = GetString(input, "sql_content");

        var approvalId = $"APPROVAL-{Guid.NewGuid():N[..8]}";

        _logger.LogWarning(
            "[MOCK] RequestApproval — ApprovalId={ApprovalId} Approver={Approver} RiskLevel={RiskLevel} Context={Context} SQL={Sql}",
            approvalId,
            approverEmail,
            riskLevel,
            context,
            sqlContent.Length > 120 ? sqlContent[..120] + "…" : sqlContent);

        var output = new Dictionary<string, object>
        {
            ["approval_request_id"] = approvalId,
            ["status"]              = "pending_approval",
            ["approver_email"]      = approverEmail
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
