using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Core.Interfaces;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly IAnalysisJobRepository    _jobRepo;
    private readonly IAnalysisResultRepository _resultRepo;

    public ReportsController(
        IAnalysisJobRepository    jobRepo,
        IAnalysisResultRepository resultRepo)
    {
        _jobRepo    = jobRepo;
        _resultRepo = resultRepo;
    }

    /// <summary>
    /// Returns paginated history of analysis jobs with their aggregate results.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetReports(
        [FromQuery] int page      = 1,
        [FromQuery] int page_size = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (page_size is < 1 or > 100) page_size = 20;

        var jobs = await _jobRepo.GetPagedAsync(page, page_size, ct);

        var items = jobs.Select(j => new
        {
            job_id         = j.Id,
            correlation_id = j.CorrelationId,
            status         = j.Status,
            input_hash     = j.InputHash,
            created_at     = j.CreatedAt,
            completed_at   = j.CompletedAt,
            result_id      = j.ResultId,
        });

        return Ok(new { page, page_size, items });
    }
}
