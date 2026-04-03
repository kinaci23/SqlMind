using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.API.Controllers;

[ApiController]
[Route("api/v1/knowledge")]
[Authorize]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(IRagService ragService, ILogger<KnowledgeController> logger)
    {
        _ragService = ragService;
        _logger     = logger;
    }

    /// <summary>
    /// Adds a document to the RAG knowledge base.
    /// The document is chunked, embedded, and stored in pgvector.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddDocument([FromBody] AddDocumentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "content is required." });

        var doc = new KnowledgeDocument
        {
            Title   = request.Title ?? "Untitled",
            Content = request.Content,
            Source  = request.Source ?? "api",
        };

        await _ragService.IndexDocumentAsync(doc, ct);

        _logger.LogInformation("Knowledge document indexed — Title={Title}", doc.Title);

        return Created(string.Empty, new { id = doc.Id, title = doc.Title });
    }

    /// <summary>
    /// Searches the knowledge base using semantic similarity.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int top_k = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "query is required." });

        var context = await _ragService.RetrieveAsync(query, top_k, ct);

        return Ok(new
        {
            chunks         = context.RetrievedChunks,
            scores         = context.Scores,
            assembled_context = context.AssembledContext,
        });
    }
}

public sealed class AddDocumentRequest
{
    public string? Title   { get; init; }
    public string  Content { get; init; } = string.Empty;
    public string? Source  { get; init; }
}
