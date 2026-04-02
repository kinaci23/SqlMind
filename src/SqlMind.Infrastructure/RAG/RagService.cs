using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.Persistence;

namespace SqlMind.Infrastructure.RAG;

/// <summary>
/// IRagService implementation.
///
/// Responsibilities:
///  1. Chunk documents (sliding window, ~500 tokens, 50-token overlap).
///  2. Embed each chunk via IEmbeddingService.
///  3. Persist chunks + embeddings to PostgreSQL/pgvector.
///  4. Retrieve top-k chunks by cosine similarity for a query.
///  5. Enforce the RAG gating rule from CLAUDE.md.
/// </summary>
public sealed class RagService : IRagService
{
    // ── Chunking constants ────────────────────────────────────────────────────
    // Approximate characters per token (conservative estimate for SQL/English mix).
    private const int CharsPerToken  = 4;
    private const int ChunkTokens    = 500;
    private const int OverlapTokens  = 50;
    private const int ChunkChars     = ChunkTokens  * CharsPerToken;  // 2000 chars
    private const int OverlapChars   = OverlapTokens * CharsPerToken; // 200 chars

    private readonly SqlMindDbContext  _db;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<RagService> _logger;

    public RagService(
        SqlMindDbContext db,
        IEmbeddingService embedding,
        ILogger<RagService> logger)
    {
        _db        = db;
        _embedding = embedding;
        _logger    = logger;
    }

    // ── IRagService ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<bool> ShouldUseRagAsync(SqlParseResult parseResult, RiskLevel riskLevel)
    {
        // CLAUDE.md gating logic — synchronous, wrapped in Task for interface compatibility.
        var tablesDetected  = parseResult.TablesDetected.Count > 0;
        var riskHighEnough  = riskLevel >= RiskLevel.MEDIUM;
        // context_needed: true when DDL/unfiltered mutations are present — these warrant extra context.
        var contextNeeded   = parseResult.HasDdlOperation || parseResult.HasUnfilteredMutation;

        var shouldUse = tablesDetected && (riskHighEnough || contextNeeded);

        _logger.LogDebug(
            "RAG gating: tables={Tables} riskLevel={Risk} contextNeeded={CtxNeeded} → shouldUse={ShouldUse}",
            tablesDetected, riskLevel, contextNeeded, shouldUse);

        return Task.FromResult(shouldUse);
    }

    /// <inheritdoc/>
    public async Task IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Indexing document '{Title}' (Id={Id}).", document.Title, document.Id);

        var chunks = ChunkDocument(document);

        _logger.LogDebug("Document split into {Count} chunks.", chunks.Count);

        // Embed all chunks (sequential — see EmbedBatchAsync for why we don't batch)
        var texts    = chunks.Select(c => c.Content).ToList();
        var vectors  = await _embedding.EmbedBatchAsync(texts, ct);

        // Persist document + chunks + embeddings in a single transaction
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.KnowledgeDocuments.Add(document);
        await _db.SaveChangesAsync(ct); // need document.Id to exist before chunks

        _db.KnowledgeChunks.AddRange(chunks);
        await _db.SaveChangesAsync(ct); // need chunk.Id before embeddings

        var records = chunks.Select((chunk, i) => new EmbeddingRecord
        {
            ChunkId   = chunk.Id,
            Vector    = vectors[i],
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();

        _db.EmbeddingRecords.AddRange(records);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Document '{Title}' indexed: {Chunks} chunks, {Embeddings} embeddings.",
            document.Title, chunks.Count, records.Count);
    }

    /// <inheritdoc/>
    public async Task<RagContext> RetrieveAsync(
        string query, int topK = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var queryVector = await _embedding.EmbedAsync(query, ct);

        // pgvector cosine distance operator: <=>
        // Lower distance = higher similarity. We convert to similarity score: 1 - distance.
        var pgVector = new Pgvector.Vector(queryVector);

        var hits = await _db.EmbeddingRecords
            .OrderBy(e => e.Vector.CosineDistance(pgVector))
            .Take(topK)
            .Include(e => e.Chunk)
            .ToListAsync(ct);

        if (hits.Count == 0)
        {
            _logger.LogDebug("RAG retrieval returned no results for query (length={Len}).", query.Length);
            return RagContext.Empty();
        }

        var chunks = hits.Select(h => h.Chunk!).ToList();

        // Approximate cosine similarity = 1 - cosine_distance.
        // We re-query the distances explicitly to surface them to the caller.
        var distances = await _db.EmbeddingRecords
            .Where(e => hits.Select(h => h.Id).Contains(e.Id))
            .Select(e => e.Vector.CosineDistance(pgVector))
            .ToListAsync(ct);

        var scores = distances.Select(d => 1f - (float)d).ToList();

        var assembled = AssembleContext(chunks);

        _logger.LogInformation(
            "RAG retrieved {Count} chunks. Top score={TopScore:F3}.",
            chunks.Count, scores.Count > 0 ? scores.Max() : 0f);

        return new RagContext
        {
            RetrievedChunks  = chunks,
            Scores           = scores,
            WasUsed          = true,
            AssembledContext = assembled
        };
    }

    // ── Chunking ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits document content into overlapping chunks.
    /// Strategy: slide a window of <see cref="ChunkChars"/> characters,
    /// stepping by (ChunkChars - OverlapChars) each time.
    /// Chunks are split on whitespace boundaries where possible.
    /// </summary>
    public static List<KnowledgeChunk> ChunkDocument(KnowledgeDocument document)
    {
        var content = document.Content;
        var chunks  = new List<KnowledgeChunk>();

        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        var step  = ChunkChars - OverlapChars;
        var start = 0;
        var index = 0;

        while (start < content.Length)
        {
            var end = Math.Min(start + ChunkChars, content.Length);

            // Try to break on a whitespace boundary to avoid mid-word splits
            if (end < content.Length)
            {
                var boundary = content.LastIndexOf(' ', end, Math.Min(end - start, 200));
                if (boundary > start)
                    end = boundary + 1; // include the space character
            }

            var chunkText = content[start..end].Trim();
            if (chunkText.Length > 0)
            {
                chunks.Add(new KnowledgeChunk
                {
                    DocumentId = document.Id,
                    Content    = chunkText,
                    ChunkIndex = index++,
                    Metadata   = new Dictionary<string, string>
                    {
                        ["source"]  = document.Source,
                        ["version"] = document.Version.ToString()
                    }
                });
            }

            start += step;
        }

        return chunks;
    }

    // ── Context assembly ──────────────────────────────────────────────────────

    private static string AssembleContext(IReadOnlyList<KnowledgeChunk> chunks)
    {
        if (chunks.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"--- Context chunk {i + 1} ---");
            sb.AppendLine(chunks[i].Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
