using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/tools")]
[Authorize]
public sealed class ToolsController : ControllerBase
{
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(IToolExecutor toolExecutor, ILogger<ToolsController> logger)
    {
        _toolExecutor = toolExecutor;
        _logger       = logger;
    }

    /// <summary>
    /// Manually executes a named tool. Requires JWT auth.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ToolExecuteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToolName))
            return BadRequest(new { error = "tool_name is required." });

        if (!Enum.TryParse<ActionType>(request.ToolName, ignoreCase: true, out var action))
        {
            return BadRequest(new
            {
                error = $"Unknown tool '{request.ToolName}'.",
                valid_tools = Enum.GetNames<ActionType>()
            });
        }

        var correlationId = Guid.NewGuid().ToString("N");
        _logger.LogInformation(
            "Manual tool execution — Tool={Tool} CorrelationId={Id}",
            request.ToolName, correlationId);

        var context = new AnalysisContext
        {
            CorrelationId = correlationId,
            SqlContent    = request.Input.TryGetValue("sql_content", out var sql) ? sql?.ToString() ?? "" : "",
            RiskLevel     = RiskLevel.HIGH,
            Findings      = [],
            ParseResult   = new SqlParseResult(),
        };

        var results = await _toolExecutor.ExecuteToolsAsync([action], context, ct);
        return Ok(new { correlation_id = correlationId, results });
    }
}

/// <summary>Request body for POST /api/v1/tools/execute.</summary>
public sealed record ToolExecuteRequest(
    string ToolName,
    Dictionary<string, object> Input);
