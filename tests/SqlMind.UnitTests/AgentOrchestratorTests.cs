using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Agent;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.UnitTests;

/// <summary>
/// Unit tests for AgentOrchestrator — verifies the ReAct loop behaviour:
/// correct tool dispatch, max-iteration guard, graceful failure handling.
/// Uses lightweight in-process test doubles (no mocking framework).
/// </summary>
public sealed class AgentOrchestratorTests
{
    // ── test doubles ──────────────────────────────────────────────────────────

    private sealed class StubPolicyEngine : IPolicyEngine
    {
        private readonly List<ActionType> _actions;
        public StubPolicyEngine(params ActionType[] actions) => _actions = [..actions];

        public Task<List<ActionType>> EvaluateAsync(RiskLevel riskLevel, CancellationToken ct = default)
            => Task.FromResult(_actions);

        public Task<PolicyConfig> GetConfigAsync() => Task.FromResult(new PolicyConfig());
    }

    private sealed class TrackingToolExecutor : IToolExecutor
    {
        public List<List<ActionType>> Calls { get; } = [];
        private readonly bool _failFirstCall;
        private int _callCount;

        public TrackingToolExecutor(bool failFirstCall = false) => _failFirstCall = failFirstCall;

        public Task<List<ToolExecutionResult>> ExecuteToolsAsync(
            List<ActionType> actions, AnalysisContext context, CancellationToken ct = default)
        {
            Calls.Add([..actions]);
            _callCount++;

            var succeed = !(_failFirstCall && _callCount == 1);
            var results = actions.Select(a => new ToolExecutionResult
            {
                ToolName  = a.ToString(),
                Success   = succeed,
                Output    = succeed ? (object)new { status = "ok" } : null,
                ErrorMessage = succeed ? null : "simulated failure"
            }).ToList();

            return Task.FromResult(results);
        }

        public Task<List<ITool>> GetAvailableToolsAsync() => Task.FromResult(new List<ITool>());
    }

    private sealed class ThrowingPolicyEngine : IPolicyEngine
    {
        public Task<List<ActionType>> EvaluateAsync(RiskLevel riskLevel, CancellationToken ct = default)
            => throw new InvalidOperationException("policy boom");

        public Task<PolicyConfig> GetConfigAsync() => Task.FromResult(new PolicyConfig());
    }

    private sealed class ThrowingToolExecutor : IToolExecutor
    {
        public Task<List<ToolExecutionResult>> ExecuteToolsAsync(
            List<ActionType> actions, AnalysisContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("executor boom");

        public Task<List<ITool>> GetAvailableToolsAsync() => Task.FromResult(new List<ITool>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AnalysisContext MakeContext(RiskLevel level) => new()
    {
        CorrelationId = Guid.NewGuid().ToString(),
        SqlContent    = "DELETE FROM users",
        RiskLevel     = level,
        Findings      = []
    };

    private static AgentOrchestrator Build(IPolicyEngine policy, IToolExecutor executor)
        => new(policy, executor, NullLogger<AgentOrchestrator>.Instance);

    // ── CRITICAL: 3 tools executed ────────────────────────────────────────────

    [Fact]
    public async Task Critical_ShouldExecute_ThreeTools()
    {
        var executor = new TrackingToolExecutor();
        var policy   = new StubPolicyEngine(
            ActionType.CreateTicket, ActionType.SendNotification, ActionType.RequestApproval);

        var orchestrator = Build(policy, executor);
        var decisions    = await orchestrator.RunAsync(MakeContext(RiskLevel.CRITICAL));

        // ToolExecutor was called once with all 3 actions
        Assert.Single(executor.Calls);
        Assert.Equal(3, executor.Calls[0].Count);
        Assert.Contains(ActionType.CreateTicket,     executor.Calls[0]);
        Assert.Contains(ActionType.SendNotification, executor.Calls[0]);
        Assert.Contains(ActionType.RequestApproval,  executor.Calls[0]);

        // Decision log contains Observe, Think, Act (×3), Observe
        Assert.Contains(decisions, d => d.DecisionType == DecisionType.Observe);
        Assert.Contains(decisions, d => d.DecisionType == DecisionType.Think);
        Assert.Contains(decisions, d => d.DecisionType == DecisionType.Act);
    }

    // ── LOW: only LogOnly ─────────────────────────────────────────────────────

    [Fact]
    public async Task Low_ShouldExecute_LogOnly_Only()
    {
        var executor = new TrackingToolExecutor();
        var policy   = new StubPolicyEngine(ActionType.LogOnly);

        var orchestrator = Build(policy, executor);
        await orchestrator.RunAsync(MakeContext(RiskLevel.LOW));

        Assert.Single(executor.Calls);
        Assert.Single(executor.Calls[0]);
        Assert.Equal(ActionType.LogOnly, executor.Calls[0][0]);
    }

    // ── Max iteration guard ───────────────────────────────────────────────────

    [Fact]
    public async Task FailingTools_ShouldNotExceedMaxIterations()
    {
        // ToolExecutor always fails → loop must not run more than 3 times
        var executor = new TrackingToolExecutor(failFirstCall: false);  // always fail via custom double
        var alwaysFailExecutor = new AlwaysFailExecutor();
        var policy   = new StubPolicyEngine(ActionType.CreateTicket);

        var orchestrator = Build(policy, alwaysFailExecutor);
        var decisions    = await orchestrator.RunAsync(MakeContext(RiskLevel.HIGH));

        // At most 3 iterations → ToolExecutor called at most 3 times
        Assert.True(alwaysFailExecutor.CallCount <= 3,
            $"Expected ≤3 iterations but got {alwaysFailExecutor.CallCount}");

        Assert.NotEmpty(decisions);
    }

    // ── No actions → loop exits immediately ───────────────────────────────────

    [Fact]
    public async Task EmptyActions_ShouldExitLoop_WithNoToolCall()
    {
        var executor     = new TrackingToolExecutor();
        var policy       = new StubPolicyEngine(); // no actions
        var orchestrator = Build(policy, executor);

        var decisions = await orchestrator.RunAsync(MakeContext(RiskLevel.MEDIUM));

        Assert.Empty(executor.Calls);
        // At least Observe + Think decisions logged
        Assert.True(decisions.Count >= 2);
    }

    // ── PolicyEngine throws → graceful abort ──────────────────────────────────

    [Fact]
    public async Task PolicyEngine_Throws_ShouldAbortGracefully()
    {
        var executor     = new TrackingToolExecutor();
        var orchestrator = Build(new ThrowingPolicyEngine(), executor);

        var decisions = await orchestrator.RunAsync(MakeContext(RiskLevel.CRITICAL));

        // No tools executed — exception was caught
        Assert.Empty(executor.Calls);
        // Decision log has at least the Observe and failed Think
        Assert.True(decisions.Count >= 2);
        Assert.Contains(decisions, d =>
            d.DecisionType == DecisionType.Think && d.Reasoning.Contains("PolicyEngine threw"));
    }

    // ── ToolExecutor throws → graceful abort ──────────────────────────────────

    [Fact]
    public async Task ToolExecutor_Throws_ShouldAbortGracefully()
    {
        var policy       = new StubPolicyEngine(ActionType.CreateTicket);
        var orchestrator = Build(policy, new ThrowingToolExecutor());

        var decisions = await orchestrator.RunAsync(MakeContext(RiskLevel.HIGH));

        // Decision log has at least Observe, Think, Act (aborted)
        Assert.True(decisions.Count >= 3);
        Assert.Contains(decisions, d =>
            d.DecisionType == DecisionType.Act && d.Reasoning.Contains("ToolExecutor threw"));
    }

    // ── nested helper ─────────────────────────────────────────────────────────

    private sealed class AlwaysFailExecutor : IToolExecutor
    {
        public int CallCount { get; private set; }

        public Task<List<ToolExecutionResult>> ExecuteToolsAsync(
            List<ActionType> actions, AnalysisContext context, CancellationToken ct = default)
        {
            CallCount++;
            var results = actions.Select(a => new ToolExecutionResult
            {
                ToolName     = a.ToString(),
                Success      = false,
                ErrorMessage = "always fails"
            }).ToList();
            return Task.FromResult(results);
        }

        public Task<List<ITool>> GetAvailableToolsAsync() => Task.FromResult(new List<ITool>());
    }
}
