using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Agent;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.UnitTests;

/// <summary>
/// Unit tests for AnalysisOrchestrator.
/// Verifies: full pipeline order, cache hit skips LLM, audit log written,
/// and failed pipeline marks job as Failed.
/// All external dependencies are stubbed — no DB, no Redis, no real LLM.
/// </summary>
public sealed class AnalysisOrchestratorTests
{
    // ─────────────────────────── stubs ───────────────────────────────────────

    private sealed class StubSqlAnalyzer : ISqlAnalyzer
    {
        public int CallCount { get; private set; }
        public Task<SqlParseResult> ParseAsync(string sql, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new SqlParseResult
            {
                OriginalSql          = sql,
                TablesDetected       = ["users"],
                HasUnfilteredMutation = false,
            });
        }
    }

    private sealed class StubRiskEvaluator : IRiskEvaluator
    {
        private readonly RiskLevel _level;
        public int CallCount { get; private set; }
        public StubRiskEvaluator(RiskLevel level = RiskLevel.HIGH) => _level = level;

        public Task<IReadOnlyList<RiskFinding>> EvaluateAsync(
            SqlParseResult p, string? llm, CancellationToken ct = default)
        {
            CallCount++;
            IReadOnlyList<RiskFinding> findings = new List<RiskFinding>
            {
                new() { Level = _level, RuleId = "RULE_01", Description = "test", IsPrimary = true, Score = 0.75f }
            };
            return Task.FromResult(findings);
        }

        public RiskLevel GetAggregateLevel(IReadOnlyList<RiskFinding> findings)
            => findings.Count > 0 ? findings.Max(f => f.Level) : RiskLevel.LOW;
    }

    private sealed class StubRagService : IRagService
    {
        public int ShouldUseCallCount { get; private set; }
        public int RetrieveCallCount  { get; private set; }
        private readonly bool _shouldUse;

        public StubRagService(bool shouldUse = false) => _shouldUse = shouldUse;

        public Task<bool> ShouldUseRagAsync(SqlParseResult p, RiskLevel r)
        {
            ShouldUseCallCount++;
            return Task.FromResult(_shouldUse);
        }

        public Task<RagContext> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default)
        {
            RetrieveCallCount++;
            return Task.FromResult(new RagContext { WasUsed = true, AssembledContext = "ctx" });
        }

        public Task IndexDocumentAsync(KnowledgeDocument doc, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubLlmClient : ILLMClient
    {
        public int CallCount { get; private set; }
        private readonly bool _throw;
        public StubLlmClient(bool throwOnCall = false) => _throw = throwOnCall;

        public Task<LlmAnalysisResult> AnalyzeAsync(LlmAnalysisRequest req, CancellationToken ct = default)
        {
            CallCount++;
            if (_throw) throw new InvalidOperationException("LLM boom");
            return Task.FromResult(new LlmAnalysisResult
            {
                BusinessSummary  = "biz",
                TechnicalSummary = "tech",
                RiskInsights     = [],
                Uncertainties    = [],
                RecommendedActions = [],
                RawJson          = "{}",
            });
        }

        public Task<string> CompleteAsync(string sys, string usr, CancellationToken ct = default)
            => Task.FromResult("{}");

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public string ProviderName => "stub";
    }

    private sealed class TrackingCacheService : ICacheService
    {
        private readonly Dictionary<string, string> _store = new();
        public int GetCount { get; private set; }
        public int SetCount { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            GetCount++;
            if (_store.TryGetValue(key, out var json))
            {
                var val = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                return Task.FromResult(val);
            }
            return Task.FromResult(default(T));
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            SetCount++;
            _store[key] = System.Text.Json.JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_store.ContainsKey(key));
    }

    private sealed class TrackingJobRepo : IAnalysisJobRepository
    {
        private readonly Dictionary<Guid, AnalysisJob> _store = new();
        public List<string> StatusHistory { get; } = [];

        public Task<AnalysisJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var job);
            return Task.FromResult(job);
        }

        public Task<AnalysisJob?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(j => j.CorrelationId == correlationId));

        public Task AddAsync(AnalysisJob job, CancellationToken ct = default)
        {
            _store[job.Id] = job;
            StatusHistory.Add(job.Status);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AnalysisJob job, CancellationToken ct = default)
        {
            _store[job.Id] = job;
            StatusHistory.Add(job.Status);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AnalysisJob>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AnalysisJob>>(_store.Values.ToList());
    }

    private sealed class TrackingResultRepo : IAnalysisResultRepository
    {
        public List<AnalysisResult> Saved { get; } = [];

        public Task<AnalysisResult?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(Saved.FirstOrDefault(r => r.JobId == jobId));

        public Task AddAsync(AnalysisResult result, CancellationToken ct = default)
        {
            Saved.Add(result);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AnalysisResult>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AnalysisResult>>(Saved);
    }

    private sealed class TrackingAuditRepo : IAuditLogRepository
    {
        public List<AuditLog> Written { get; } = [];

        public Task AddAsync(AuditLog log, CancellationToken ct = default)
        {
            Written.Add(log);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditLog>>(Written.Where(l => l.CorrelationId == correlationId).ToList());
    }

    private sealed class NoOpPolicyEngine : IPolicyEngine
    {
        public Task<List<ActionType>> EvaluateAsync(RiskLevel r, CancellationToken ct = default)
            => Task.FromResult(new List<ActionType>());
        public Task<PolicyConfig> GetConfigAsync() => Task.FromResult(new PolicyConfig());
    }

    private sealed class NoOpToolExecutor : IToolExecutor
    {
        public Task<List<ToolExecutionResult>> ExecuteToolsAsync(
            List<ActionType> actions, AnalysisContext ctx, CancellationToken ct = default)
            => Task.FromResult(new List<ToolExecutionResult>());
        public Task<List<ITool>> GetAvailableToolsAsync() => Task.FromResult(new List<ITool>());
    }

    // ─────────────────────────── helpers ─────────────────────────────────────

    private static AnalysisJob MakeJob(string sql = "DELETE FROM users WHERE id=1")
    {
        var job = new AnalysisJob
        {
            SqlContent = sql,
            InputHash  = AnalysisOrchestrator.ComputeHash(sql),
        };
        return job;
    }

    private static AnalysisOrchestrator Build(
        ISqlAnalyzer?             sqlAnalyzer   = null,
        IRiskEvaluator?           riskEvaluator = null,
        ICacheService?            cache         = null,
        IRagService?              ragService    = null,
        ILLMClient?              llmClient     = null,
        IAnalysisJobRepository?   jobRepo       = null,
        IAnalysisResultRepository? resultRepo   = null,
        IAuditLogRepository?      auditRepo     = null)
    {
        var agentOrch = new AgentOrchestrator(
            new NoOpPolicyEngine(),
            new NoOpToolExecutor(),
            NullLogger<AgentOrchestrator>.Instance);

        return new AnalysisOrchestrator(
            sqlAnalyzer   ?? new StubSqlAnalyzer(),
            riskEvaluator ?? new StubRiskEvaluator(),
            cache         ?? new TrackingCacheService(),
            ragService    ?? new StubRagService(),
            llmClient     ?? new StubLlmClient(),
            agentOrch,
            jobRepo       ?? new TrackingJobRepo(),
            resultRepo    ?? new TrackingResultRepo(),
            auditRepo     ?? new TrackingAuditRepo(),
            NullLogger<AnalysisOrchestrator>.Instance);
    }

    // ─────────────────────────── tests ───────────────────────────────────────

    [Fact]
    public async Task Pipeline_ShouldComplete_AndPersistResult()
    {
        var jobRepo    = new TrackingJobRepo();
        var resultRepo = new TrackingResultRepo();
        var auditRepo  = new TrackingAuditRepo();
        var job        = MakeJob();

        await jobRepo.AddAsync(job);

        var orchestrator = Build(jobRepo: jobRepo, resultRepo: resultRepo, auditRepo: auditRepo);
        await orchestrator.RunAsync(job.Id);

        // Job marked Completed
        Assert.Contains("Completed", jobRepo.StatusHistory);

        // Result persisted
        Assert.Single(resultRepo.Saved);
        Assert.Equal(job.Id, resultRepo.Saved[0].JobId);

        // Audit log written
        Assert.Single(auditRepo.Written);
        Assert.Equal(job.CorrelationId, auditRepo.Written[0].CorrelationId);
    }

    [Fact]
    public async Task CacheHit_ShouldSkipLlmCall()
    {
        var cache   = new TrackingCacheService();
        var llm     = new StubLlmClient();
        var jobRepo = new TrackingJobRepo();
        var job     = MakeJob("SELECT * FROM users");

        await jobRepo.AddAsync(job);

        // Pre-populate cache with a valid LlmAnalysisResult
        var cachedResult = new LlmAnalysisResult
        {
            BusinessSummary  = "cached",
            TechnicalSummary = "cached",
            RiskInsights     = [],
            Uncertainties    = [],
            RecommendedActions = [],
            RawJson          = "{}",
        };
        await cache.SetAsync($"llm:{job.InputHash}", cachedResult);

        var orchestrator = Build(cache: cache, llmClient: llm, jobRepo: jobRepo);
        await orchestrator.RunAsync(job.Id);

        // LLM must NOT have been called
        Assert.Equal(0, llm.CallCount);
        // Cache was read
        Assert.True(cache.GetCount >= 1);
    }

    [Fact]
    public async Task AuditLog_ShouldContain_CorrelationId_And_InputHash()
    {
        var auditRepo = new TrackingAuditRepo();
        var jobRepo   = new TrackingJobRepo();
        var job       = MakeJob("UPDATE orders SET status='done'");

        await jobRepo.AddAsync(job);

        var orchestrator = Build(jobRepo: jobRepo, auditRepo: auditRepo);
        await orchestrator.RunAsync(job.Id);

        Assert.Single(auditRepo.Written);
        var log = auditRepo.Written[0];
        Assert.Equal(job.CorrelationId, log.CorrelationId);
        Assert.Equal(job.InputHash,     log.InputHash);
        Assert.False(string.IsNullOrEmpty(log.SqlParseResult));
        Assert.False(string.IsNullOrEmpty(log.RuleTriggers));
    }

    [Fact]
    public async Task LlmFailure_ShouldMarkJob_Failed_AndWriteAuditLog()
    {
        var jobRepo   = new TrackingJobRepo();
        var auditRepo = new TrackingAuditRepo();
        var job       = MakeJob("DROP TABLE users");

        await jobRepo.AddAsync(job);

        var orchestrator = Build(
            llmClient : new StubLlmClient(throwOnCall: true),
            jobRepo   : jobRepo,
            auditRepo : auditRepo);

        await orchestrator.RunAsync(job.Id);

        // Job must be Failed
        Assert.Contains("Failed", jobRepo.StatusHistory);

        // Failure audit log still written
        Assert.Single(auditRepo.Written);
        Assert.Contains("ERROR", auditRepo.Written[0].ToolExecution);
    }

    [Fact]
    public async Task RagGated_WhenShouldUseRag_RetrieveIsCalled()
    {
        var ragService = new StubRagService(shouldUse: true);
        var jobRepo    = new TrackingJobRepo();
        var job        = MakeJob("DELETE FROM logs");

        await jobRepo.AddAsync(job);

        var orchestrator = Build(ragService: ragService, jobRepo: jobRepo);
        await orchestrator.RunAsync(job.Id);

        Assert.Equal(1, ragService.ShouldUseCallCount);
        Assert.Equal(1, ragService.RetrieveCallCount);
    }

    [Fact]
    public async Task RagSkipped_WhenShouldUseRag_ReturnsFalse()
    {
        var ragService = new StubRagService(shouldUse: false);
        var jobRepo    = new TrackingJobRepo();
        var job        = MakeJob("SELECT id FROM users");

        await jobRepo.AddAsync(job);

        var orchestrator = Build(ragService: ragService, jobRepo: jobRepo);
        await orchestrator.RunAsync(job.Id);

        Assert.Equal(1, ragService.ShouldUseCallCount);
        Assert.Equal(0, ragService.RetrieveCallCount);
    }

    [Fact]
    public async Task ComputeHash_ShouldBeDeterministic()
    {
        var sql  = "SELECT 1";
        var h1   = AnalysisOrchestrator.ComputeHash(sql);
        var h2   = AnalysisOrchestrator.ComputeHash(sql);
        var diff = AnalysisOrchestrator.ComputeHash("SELECT 2");

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, diff);
        Assert.Equal(64, h1.Length); // SHA-256 hex = 64 chars
    }
}
