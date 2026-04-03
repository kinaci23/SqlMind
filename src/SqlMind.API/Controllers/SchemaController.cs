using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Core.Interfaces;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/schema")]
[Authorize]
public sealed class SchemaController : ControllerBase
{
    private readonly ISchemaIngestionService _ingestion;
    private readonly ILogger<SchemaController> _logger;

    public SchemaController(
        ISchemaIngestionService ingestion,
        ILogger<SchemaController> logger)
    {
        _ingestion = ingestion;
        _logger    = logger;
    }

    /// <summary>
    /// Connects to the given database, reads its public schema, and indexes
    /// every table as a KnowledgeDocument for RAG-based analysis context.
    /// This is an expensive operation — rate limited to 5 requests/hour.
    /// </summary>
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest(
        [FromBody] SchemaIngestRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest(new { error = "connection_string is required." });

        if (string.IsNullOrWhiteSpace(request.Environment))
            return BadRequest(new { error = "environment is required." });

        _logger.LogInformation(
            "Schema ingest requested — environment={Environment}", request.Environment);

        var result = await _ingestion.IngestAsync(
            request.ConnectionString,
            request.Environment,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Returns the list of table names already ingested for the given environment.
    /// </summary>
    [HttpGet("tables")]
    public async Task<IActionResult> GetTables(
        [FromQuery] string environment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return BadRequest(new { error = "environment query parameter is required." });

        var tables = await _ingestion.GetIngestedTablesAsync(environment, ct);

        return Ok(new { environment, tables, count = tables.Count });
    }
}

public sealed class SchemaIngestRequest
{
    public string ConnectionString { get; init; } = string.Empty;
    public string Environment      { get; init; } = string.Empty;
}
