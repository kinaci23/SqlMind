namespace SqlMind.Core.Models;

/// <summary>
/// API input model for POST /api/v1/analyze.
/// Contains everything the caller provides; the pipeline fills in the rest at runtime.
/// </summary>
public sealed class AnalysisRequest
{
    /// <summary>The SQL script to analyse (required).</summary>
    public string SqlContent { get; init; } = string.Empty;

    /// <summary>Target database dialect hint (e.g. "postgresql", "mssql"). Optional.</summary>
    public string? DbDialect { get; init; }

    /// <summary>Deployment environment (e.g. "production", "staging"). Optional.</summary>
    public string? Environment { get; init; }

    /// <summary>Identity of the requester — propagated to audit logs.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>Per-request behavioural overrides.</summary>
    public AnalysisOptions Options { get; init; } = new();
}

/// <summary>Per-request behavioural flags that override server defaults.</summary>
public sealed class AnalysisOptions
{
    /// <summary>Force RAG retrieval regardless of gating logic. Default: false (gating applies).</summary>
    public bool UseRag { get; init; } = false;

    /// <summary>Allow PolicyEngine to execute tools automatically. Default: true.</summary>
    public bool AutoAction { get; init; } = true;

    /// <summary>Minimum risk level that triggers tool execution. Default: HIGH.</summary>
    public string RiskThreshold { get; init; } = "HIGH";
}
