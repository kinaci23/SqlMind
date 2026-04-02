using Microsoft.Extensions.Logging;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Tools;

/// <summary>
/// Orchestrates tool execution after IPolicyEngine approval.
/// Maps ActionType → ITool via the DI-injected tool registry.
/// WarnLog and LogOnly are handled internally (no external tool call).
/// Every execution result is logged for audit purposes.
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IReadOnlyDictionary<string, ITool> _toolRegistry;
    private readonly ILogger<ToolExecutor> _logger;

    /// <summary>
    /// ActionType → tool Name mapping (must stay in sync with ITool.Name values).
    /// </summary>
    private static readonly IReadOnlyDictionary<ActionType, string> ActionToToolName =
        new Dictionary<ActionType, string>
        {
            [ActionType.CreateTicket]     = "create_ticket",
            [ActionType.SendNotification] = "send_notification",
            [ActionType.RequestApproval]  = "request_approval"
        };

    public ToolExecutor(IEnumerable<ITool> tools, ILogger<ToolExecutor> logger)
    {
        _toolRegistry = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger       = logger;
    }

    public async Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ActionType> actions,
        AnalysisContext context,
        CancellationToken ct = default)
    {
        var results = new List<ToolExecutionResult>(actions.Count);

        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();

            // Lightweight actions handled without an external tool call
            if (action == ActionType.WarnLog)
            {
                _logger.LogWarning(
                    "[WarnLog] CorrelationId={CorrelationId} RiskLevel={RiskLevel} Findings={Count}",
                    context.CorrelationId, context.RiskLevel, context.Findings.Count);

                results.Add(InternalResult(action, context.CorrelationId, "warn_log_written"));
                continue;
            }

            if (action == ActionType.LogOnly)
            {
                _logger.LogInformation(
                    "[LogOnly] CorrelationId={CorrelationId} RiskLevel={RiskLevel}",
                    context.CorrelationId, context.RiskLevel);

                results.Add(InternalResult(action, context.CorrelationId, "log_written"));
                continue;
            }

            // External tool execution
            if (!ActionToToolName.TryGetValue(action, out var toolName) ||
                !_toolRegistry.TryGetValue(toolName, out var tool))
            {
                _logger.LogError("No ITool registered for ActionType={Action}.", action);
                results.Add(FailedResult(action.ToString(), context.CorrelationId, $"No tool for action {action}"));
                continue;
            }

            var input = BuildInput(action, context);

            _logger.LogInformation(
                "Executing tool={Tool} CorrelationId={CorrelationId}",
                toolName, context.CorrelationId);

            ToolExecutionResult result;
            try
            {
                var raw = await tool.ExecuteAsync(input, ct);
                result = new ToolExecutionResult
                {
                    ToolName      = raw.ToolName,
                    CorrelationId = context.CorrelationId,
                    Success       = raw.Success,
                    Output        = raw.Output,
                    ErrorMessage  = raw.ErrorMessage,
                    ExecutedAt    = raw.ExecutedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool={Tool} threw an unhandled exception.", toolName);
                result = FailedResult(toolName, context.CorrelationId, ex.Message);
            }

            _logger.LogInformation(
                "Tool={Tool} Success={Success} CorrelationId={CorrelationId}",
                toolName, result.Success, context.CorrelationId);

            results.Add(result);
        }

        return results;
    }

    public Task<List<ITool>> GetAvailableToolsAsync()
        => Task.FromResult(_toolRegistry.Values.ToList());

    // ── private helpers ───────────────────────────────────────────────────────

    private static Dictionary<string, object> BuildInput(ActionType action, AnalysisContext ctx)
    {
        var findingSummary = ctx.Findings.Count > 0
            ? string.Join("; ", ctx.Findings.Select(f => f.Description))
            : "No detailed findings.";

        return action switch
        {
            ActionType.CreateTicket => new Dictionary<string, object>
            {
                ["title"]       = $"[{ctx.RiskLevel}] SQL Risk Detected — {ctx.CorrelationId}",
                ["description"] = findingSummary,
                ["priority"]    = ctx.RiskLevel.ToString(),
                ["sql_content"] = ctx.SqlContent,
                ["risk_level"]  = ctx.RiskLevel.ToString()
            },
            ActionType.SendNotification => new Dictionary<string, object>
            {
                ["message"]    = $"SQL risk alert [{ctx.RiskLevel}]: {findingSummary}",
                ["channel"]    = "alerts",
                ["risk_level"] = ctx.RiskLevel.ToString()
            },
            ActionType.RequestApproval => new Dictionary<string, object>
            {
                ["approver_email"] = "dba-team@company.com",
                ["context"]        = findingSummary,
                ["sql_content"]    = ctx.SqlContent,
                ["risk_level"]     = ctx.RiskLevel.ToString()
            },
            _ => []
        };
    }

    private static ToolExecutionResult InternalResult(ActionType action, string correlationId, string status)
        => new()
        {
            ToolName      = action.ToString().ToLowerInvariant(),
            CorrelationId = correlationId,
            Success       = true,
            Output        = new Dictionary<string, object> { ["status"] = status },
            ExecutedAt    = DateTimeOffset.UtcNow
        };

    private static ToolExecutionResult FailedResult(string toolName, string correlationId, string error)
        => new()
        {
            ToolName      = toolName,
            CorrelationId = correlationId,
            Success       = false,
            ErrorMessage  = error,
            ExecutedAt    = DateTimeOffset.UtcNow
        };
}
