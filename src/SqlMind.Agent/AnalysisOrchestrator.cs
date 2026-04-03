using Microsoft.Extensions.Logging;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqlMind.Agent;

/// <summary>
/// Coordinates the full analysis pipeline end-to-end:
///   1. SQL parse   (ISqlAnalyzer)
///   2. Rule-based risk   (IRiskEvaluator)
///   3. Cache check   (ICacheService) — skip LLM on hit
///   4. RAG gating   (IRagService.ShouldUseRagAsync) → retrieve if needed
///   5. LLM analysis   (ILLMClient)
///   6. Final risk merge
///   7. Agent ReAct loop   (AgentOrchestrator) — policy + tool execution
///   8. Audit log write   (IAuditLogRepository)
///   9. AnalysisResult persist   (IAnalysisResultRepository)
///
/// This class is the single entry point for background jobs.
/// </summary>
public sealed class AnalysisOrchestrator
{
    private readonly ISqlAnalyzer             _sqlAnalyzer;
    private readonly IRiskEvaluator           _riskEvaluator;
    private readonly ICacheService            _cache;
    private readonly IRagService              _ragService;
    private readonly ILLMClient              _llmClient;
    private readonly AgentOrchestrator       _agentOrchestrator;
    private readonly IAnalysisJobRepository   _jobRepo;
    private readonly IAnalysisResultRepository _resultRepo;
    private readonly IAuditLogRepository      _auditRepo;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public AnalysisOrchestrator(
        ISqlAnalyzer                    sqlAnalyzer,
        IRiskEvaluator                  riskEvaluator,
        ICacheService                   cache,
        IRagService                     ragService,
        ILLMClient                     llmClient,
        AgentOrchestrator              agentOrchestrator,
        IAnalysisJobRepository          jobRepo,
        IAnalysisResultRepository       resultRepo,
        IAuditLogRepository             auditRepo,
        ILogger<AnalysisOrchestrator>  logger)
    {
        _sqlAnalyzer       = sqlAnalyzer;
        _riskEvaluator     = riskEvaluator;
        _cache             = cache;
        _ragService        = ragService;
        _llmClient         = llmClient;
        _agentOrchestrator = agentOrchestrator;
        _jobRepo           = jobRepo;
        _resultRepo        = resultRepo;
        _auditRepo         = auditRepo;
        _logger            = logger;
    }

    /// <summary>
    /// Executes the full pipeline for a queued analysis job.
    /// Called by Hangfire — job status is updated in the analysis_jobs table.
    /// </summary>
    public async Task RunAsync(Guid jobId, CancellationToken ct = default)
    {
        var sw  = System.Diagnostics.Stopwatch.StartNew();
        var job = await _jobRepo.GetByIdAsync(jobId, ct);

        if (job is null)
        {
            _logger.LogError("AnalysisOrchestrator: job {JobId} not found.", jobId);
            return;
        }

        _logger.LogInformation(
            "Pipeline starting — JobId={JobId} CorrelationId={CorrelationId}",
            jobId, job.CorrelationId);

        job.Status = "Processing";
        await _jobRepo.UpdateAsync(job, ct);

        // Audit state accumulated throughout the pipeline
        string sqlParseJson   = "{}";
        string ruleTriggersJson = "[]";
        string llmOutputRaw   = string.Empty;
        bool   ragUsed        = false;
        string toolResultsJson = "[]";

        try
        {
            // ── 1. SQL PARSE ──────────────────────────────────────────────────
            _logger.LogDebug("Step 1: SQL parse — CorrelationId={Id}", job.CorrelationId);
            var parseResult = await _sqlAnalyzer.ParseAsync(job.SqlContent, ct);
            sqlParseJson    = JsonSerializer.Serialize(parseResult, _json);

            // ── 2. RULE-BASED RISK ────────────────────────────────────────────
            _logger.LogDebug("Step 2: Rule-based risk — CorrelationId={Id}", job.CorrelationId);
            var findings    = await _riskEvaluator.EvaluateAsync(parseResult, null, ct);
            var riskLevel   = _riskEvaluator.GetAggregateLevel(findings);
            ruleTriggersJson = JsonSerializer.Serialize(findings.Select(f => f.RuleId), _json);

            _logger.LogInformation(
                "Rule-based risk: {Level} ({Count} findings) — CorrelationId={Id}",
                riskLevel, findings.Count, job.CorrelationId);

            // ── 3. CACHE CHECK ────────────────────────────────────────────────
            var cacheKey     = $"llm:{job.InputHash}";
            LlmAnalysisResult? llmResult = await _cache.GetAsync<LlmAnalysisResult>(cacheKey, ct);

            if (llmResult is not null)
            {
                _logger.LogInformation(
                    "Cache HIT for InputHash={Hash} — skipping LLM. CorrelationId={Id}",
                    job.InputHash, job.CorrelationId);
                llmOutputRaw = llmResult.RawJson;
            }
            else
            {
                // ── 4. RAG GATING ─────────────────────────────────────────────
                _logger.LogDebug("Step 4: RAG gating — CorrelationId={Id}", job.CorrelationId);
                RagContext ragContext = new() { WasUsed = false };

                if (await _ragService.ShouldUseRagAsync(parseResult, riskLevel))
                {
                    _logger.LogInformation("RAG triggered — CorrelationId={Id}", job.CorrelationId);
                    ragContext = await _ragService.RetrieveAsync(job.SqlContent, topK: 5, ct: ct);
                    ragUsed   = ragContext.WasUsed;
                }

                // ── 5. LLM ANALYSIS ───────────────────────────────────────────
                _logger.LogDebug("Step 5: LLM analysis — CorrelationId={Id}", job.CorrelationId);
                var llmRequest = new LlmAnalysisRequest
                {
                    ParseResult          = parseResult,
                    RuleBasedRiskLevel   = riskLevel,
                    RagContext           = ragContext.AssembledContext,
                    CorrelationId        = job.CorrelationId,
                };

                llmResult    = await _llmClient.AnalyzeAsync(llmRequest, ct);
                llmOutputRaw = llmResult.RawJson;

                // Cache the validated LLM output
                await _cache.SetAsync(cacheKey, llmResult, TimeSpan.FromHours(1), ct);
            }

            // ── 6. FINAL RISK (rule PRIMARY, LLM can only raise, never lower) ──
            _logger.LogDebug("Step 6: Final risk merge — CorrelationId={Id}", job.CorrelationId);
            var finalRisk = riskLevel; // LLM cannot lower a CRITICAL rule-based finding

            // ── 7. AGENT REACT LOOP ───────────────────────────────────────────
            _logger.LogDebug("Step 7: Agent loop — CorrelationId={Id}", job.CorrelationId);
            var agentContext = new AnalysisContext
            {
                CorrelationId = job.CorrelationId,
                SqlContent    = job.SqlContent,
                RiskLevel     = finalRisk,
                Findings      = findings,
                ParseResult   = parseResult,
            };

            var decisions   = await _agentOrchestrator.RunAsync(agentContext, ct);
            var executedTools = decisions
                .Where(d => d.DecisionType == DecisionType.Act && d.ToolName is not null)
                .Select(d => d.ToolName!)
                .ToList();

            toolResultsJson = JsonSerializer.Serialize(executedTools, _json);

            // ── 8. AUDIT LOG ──────────────────────────────────────────────────
            _logger.LogDebug("Step 8: Writing audit log — CorrelationId={Id}", job.CorrelationId);
            var auditLog = new AuditLog
            {
                CorrelationId  = job.CorrelationId,
                InputHash      = job.InputHash,
                SqlParseResult = sqlParseJson,
                RuleTriggers   = ruleTriggersJson,
                LlmOutput      = llmOutputRaw,
                RagUsed        = ragUsed,
                ToolExecution  = toolResultsJson,
                Timestamp      = DateTimeOffset.UtcNow,
            };
            await _auditRepo.AddAsync(auditLog, ct);

            // ── 9. PERSIST RESULT ─────────────────────────────────────────────
            _logger.LogDebug("Step 9: Persisting result — CorrelationId={Id}", job.CorrelationId);
            var result = new AnalysisResult
            {
                JobId              = job.Id,
                CorrelationId      = job.CorrelationId,
                LlmOutput          = llmOutputRaw,
                AggregateRiskLevel = finalRisk,
                Findings           = findings,
                RagUsed            = ragUsed,
                ExecutedTools      = executedTools,
            };
            await _resultRepo.AddAsync(result, ct);

            // ── UPDATE JOB STATUS ─────────────────────────────────────────────
            job.Status      = "Completed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ResultId    = result.Id;
            await _jobRepo.UpdateAsync(job, ct);

            sw.Stop();
            _logger.LogInformation(
                "Pipeline complete — JobId={JobId} Risk={Risk} ProcessingMs={Ms} CorrelationId={Id}",
                jobId, finalRisk, sw.ElapsedMilliseconds, job.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed — JobId={JobId} CorrelationId={Id}", jobId, job.CorrelationId);

            job.Status      = "Failed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _jobRepo.UpdateAsync(job, ct);

            // Write a partial audit log even on failure
            try
            {
                await _auditRepo.AddAsync(new AuditLog
                {
                    CorrelationId  = job.CorrelationId,
                    InputHash      = job.InputHash,
                    SqlParseResult = sqlParseJson,
                    RuleTriggers   = ruleTriggersJson,
                    LlmOutput      = llmOutputRaw,
                    RagUsed        = ragUsed,
                    ToolExecution  = $"[\"ERROR: {ex.Message}\"]",
                    Timestamp      = DateTimeOffset.UtcNow,
                }, ct);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to write failure audit log for CorrelationId={Id}", job.CorrelationId);
            }
        }
    }

    /// <summary>
    /// Computes the SHA-256 hex hash of the SQL content.
    /// Used as the Redis cache key for LLM responses.
    /// </summary>
    public static string ComputeHash(string sqlContent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sqlContent));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
