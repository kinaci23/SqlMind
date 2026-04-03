using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Agent;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;
using System.Text.Json;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/analyze")]
[Authorize]
public sealed class AnalysisController : ControllerBase
{
    private readonly IAnalysisJobRepository    _jobRepo;
    private readonly IAnalysisResultRepository _resultRepo;
    private readonly IBackgroundJobService     _jobs;
    private readonly ILogger<AnalysisController> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public AnalysisController(
        IAnalysisJobRepository      jobRepo,
        IAnalysisResultRepository   resultRepo,
        IBackgroundJobService       jobs,
        ILogger<AnalysisController> logger)
    {
        _jobRepo    = jobRepo;
        _resultRepo = resultRepo;
        _jobs       = jobs;
        _logger     = logger;
    }

    /// <summary>
    /// Submits a SQL script for async analysis. Returns immediately with a job_id.
    /// Poll GET /api/v1/analyze/{job_id} for the result.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] AnalysisRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SqlContent))
            return BadRequest(new { error = "sql_content is required." });

        var inputHash = AnalysisOrchestrator.ComputeHash(request.SqlContent);

        var job = new AnalysisJob
        {
            SqlContent    = request.SqlContent,
            InputHash     = inputHash,
            Status        = "Enqueued",
        };

        await _jobRepo.AddAsync(job, ct);

        var hangfireJobId = _jobs.Enqueue<AnalysisOrchestrator>(o => o.RunAsync(job.Id, CancellationToken.None));

        job.BackgroundJobId = hangfireJobId;
        await _jobRepo.UpdateAsync(job, ct);

        _logger.LogInformation(
            "Analysis job enqueued — JobId={JobId} HangfireId={HId} CorrelationId={CId}",
            job.Id, hangfireJobId, job.CorrelationId);

        return Accepted(new
        {
            job_id         = job.Id,
            correlation_id = job.CorrelationId,
            status         = job.Status,
        });
    }

    /// <summary>
    /// Returns the current state of an analysis job.
    /// If completed, the full AnalysisReport is included in the response.
    /// </summary>
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetResult(Guid jobId, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null)
            return NotFound(new { error = $"Job {jobId} not found." });

        if (job.Status != "Completed")
        {
            return Ok(new
            {
                job_id         = job.Id,
                correlation_id = job.CorrelationId,
                status         = job.Status,
            });
        }

        var result = await _resultRepo.GetByJobIdAsync(jobId, ct);
        if (result is null)
            return Ok(new { job_id = job.Id, status = job.Status, report = (object?)null });

        var report = BuildReport(job, result);
        return Ok(new
        {
            job_id         = job.Id,
            correlation_id = job.CorrelationId,
            status         = job.Status,
            report,
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AnalysisReport BuildReport(AnalysisJob job, AnalysisResult result)
    {
        LlmAnalysisResult? llm = null;
        if (!string.IsNullOrEmpty(result.LlmOutput))
        {
            try { llm = JsonSerializer.Deserialize<LlmAnalysisResult>(result.LlmOutput, _json); }
            catch { /* non-fatal — partial report */ }
        }

        SqlParseResult? parse = null;
        if (!string.IsNullOrEmpty(job.ParseResultJson))
        {
            try { parse = JsonSerializer.Deserialize<SqlParseResult>(job.ParseResultJson, _json); }
            catch { /* non-fatal */ }
        }

        // Risk score: midpoint of band
        var riskScore = result.AggregateRiskLevel switch
        {
            RiskLevel.CRITICAL => 0.95f,
            RiskLevel.HIGH     => 0.75f,
            RiskLevel.MEDIUM   => 0.45f,
            _                  => 0.10f,
        };

        return new AnalysisReport
        {
            JobId             = job.Id,
            CorrelationId     = result.CorrelationId,
            SummaryBusiness   = llm?.BusinessSummary   ?? string.Empty,
            SummaryTechnical  = llm?.TechnicalSummary  ?? string.Empty,
            Operations        = parse?.Operations.Select(o => o.ToString()).ToList() ?? [],
            AffectedTables    = parse?.TablesDetected.ToList() ?? [],
            AffectedColumns   = [],
            RiskLevel         = result.AggregateRiskLevel.ToString(),
            RiskScore         = riskScore,
            RiskReasons       = result.Findings.Select(f => f.Description).ToList(),
            RuleTriggers      = result.Findings.Where(f => f.IsPrimary).Select(f => f.RuleId).ToList(),
            LlmInsights       = llm?.RiskInsights   ?? [],
            Confidence        = 1.0f - (llm is null ? 0.5f : 0f),
            RagUsed           = result.RagUsed,
            RecommendedActions = llm?.RecommendedActions ?? [],
            ExecutedActions   = result.ExecutedTools,
            ProcessingTimeMs  = job.CompletedAt.HasValue
                ? (long)(job.CompletedAt.Value - job.CreatedAt).TotalMilliseconds
                : 0,
            CreatedAt         = result.CreatedAt,
        };
    }
}
