using Microsoft.Extensions.Logging;
using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Agent;

/// <summary>
/// Orchestrates the agent's ReAct loop (Observe → Think → Act → Observe).
/// This is NOT an autonomous agent — every action decision is delegated to IPolicyEngine.
/// The orchestrator is responsible for loop control, decision logging, and graceful failure handling.
/// Max iterations: 3 (prevents infinite loops).
/// </summary>
public sealed class AgentOrchestrator
{
    private const int MaxIterations = 3;

    private readonly IPolicyEngine _policyEngine;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IPolicyEngine policyEngine,
        IToolExecutor toolExecutor,
        ILogger<AgentOrchestrator> logger)
    {
        _policyEngine = policyEngine;
        _toolExecutor = toolExecutor;
        _logger       = logger;
    }

    /// <summary>
    /// Runs the ReAct loop for the given analysis context.
    /// Returns the full ordered decision log for audit purposes.
    /// </summary>
    public async Task<IReadOnlyList<AgentDecision>> RunAsync(
        AnalysisContext context,
        CancellationToken ct = default)
    {
        var decisions = new List<AgentDecision>();
        var iteration = 0;

        _logger.LogInformation(
            "AgentOrchestrator starting — CorrelationId={CorrelationId} RiskLevel={RiskLevel}",
            context.CorrelationId, context.RiskLevel);

        while (iteration < MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iteration++;

            _logger.LogDebug("ReAct iteration {Iteration}/{Max}", iteration, MaxIterations);

            // ── OBSERVE ──────────────────────────────────────────────────────
            var observe = new AgentDecision
            {
                DecisionType = DecisionType.Observe,
                Reasoning    = iteration == 1
                    ? $"Initial observation: RiskLevel={context.RiskLevel}, Findings={context.Findings.Count}"
                    : $"Re-observation after iteration {iteration - 1}",
            };
            decisions.Add(observe);
            _logger.LogDebug("OBSERVE: {Reasoning}", observe.Reasoning);

            // ── THINK ────────────────────────────────────────────────────────
            List<ActionType> approvedActions;
            try
            {
                approvedActions = await _policyEngine.EvaluateAsync(context.RiskLevel, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PolicyEngine.EvaluateAsync failed on iteration {Iteration}.", iteration);
                decisions.Add(new AgentDecision
                {
                    DecisionType = DecisionType.Think,
                    Reasoning    = $"PolicyEngine threw: {ex.Message} — aborting loop."
                });
                break;
            }

            var think = new AgentDecision
            {
                DecisionType = DecisionType.Think,
                Reasoning    = $"PolicyEngine approved {approvedActions.Count} action(s): [{string.Join(", ", approvedActions)}]"
            };
            decisions.Add(think);
            _logger.LogDebug("THINK: {Reasoning}", think.Reasoning);

            // Nothing to do — end the loop
            if (approvedActions.Count == 0)
            {
                _logger.LogInformation("No actions approved — ReAct loop complete.");
                break;
            }

            // ── ACT ──────────────────────────────────────────────────────────
            foreach (var action in approvedActions)
            {
                var toolName = action.ToString().ToLowerInvariant();
                decisions.Add(new AgentDecision
                {
                    DecisionType = DecisionType.Act,
                    ToolName     = toolName,
                    ToolInput    = null, // concrete input built inside ToolExecutor
                    Reasoning    = $"Executing action={action} approved by PolicyEngine for RiskLevel={context.RiskLevel}"
                });
            }

            List<ToolExecutionResult> results;
            try
            {
                results = await _toolExecutor.ExecuteToolsAsync(approvedActions, context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ToolExecutor.ExecuteToolsAsync failed on iteration {Iteration}.", iteration);
                decisions.Add(new AgentDecision
                {
                    DecisionType = DecisionType.Act,
                    Reasoning    = $"ToolExecutor threw: {ex.Message} — aborting loop."
                });
                break;
            }

            // ── OBSERVE (post-act) ───────────────────────────────────────────
            var successCount = results.Count(r => r.Success);
            var failCount    = results.Count - successCount;

            decisions.Add(new AgentDecision
            {
                DecisionType = DecisionType.Observe,
                Reasoning    = $"Post-act observation: {successCount} succeeded, {failCount} failed."
            });

            _logger.LogInformation(
                "Iteration {Iteration} complete — Success={Success} Failed={Failed} CorrelationId={CorrelationId}",
                iteration, successCount, failCount, context.CorrelationId);

            // All succeeded — no need to iterate further
            if (failCount == 0)
                break;

            // Partial failures — continue loop up to MaxIterations for retry signal
            _logger.LogWarning(
                "{FailCount} tool(s) failed on iteration {Iteration}. Will observe and re-evaluate if iterations remain.",
                failCount, iteration);
        }

        if (iteration >= MaxIterations)
        {
            _logger.LogWarning(
                "AgentOrchestrator reached MaxIterations={Max} — loop terminated. CorrelationId={CorrelationId}",
                MaxIterations, context.CorrelationId);
        }

        _logger.LogInformation(
            "AgentOrchestrator finished — {DecisionCount} decisions logged. CorrelationId={CorrelationId}",
            decisions.Count, context.CorrelationId);

        return decisions.AsReadOnly();
    }
}
