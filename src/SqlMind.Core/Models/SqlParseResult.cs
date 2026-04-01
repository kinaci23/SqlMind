using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// AST-like output from ISqlAnalyzer. This is the PRIMARY input to the risk pipeline.
/// All downstream layers (RAG gating, LLM, risk evaluator) consume this.
/// </summary>
public sealed class SqlParseResult
{
    public string OriginalSql { get; init; } = string.Empty;
    public string NormalizedSql { get; init; } = string.Empty;
    public IReadOnlyList<OperationType> Operations { get; init; } = [];
    public IReadOnlyList<string> TablesDetected { get; init; } = [];

    /// <summary>True if any WHERE clause is missing on a mutating statement.</summary>
    public bool HasUnfilteredMutation { get; init; }

    /// <summary>True if any DDL operation (DROP, ALTER, TRUNCATE) is present.</summary>
    public bool HasDdlOperation { get; init; }

    /// <summary>Estimated row impact if available (e.g., from subquery analysis).</summary>
    public long? EstimatedRowImpact { get; init; }

    public IReadOnlyList<string> ParseWarnings { get; init; } = [];

    /// <summary>True if at least one WHERE clause is present in any statement.</summary>
    public bool WhereClauseExists { get; init; }

    /// <summary>True if at least one JOIN is present in any statement.</summary>
    public bool JoinExists { get; init; }

    /// <summary>True if a DROP TABLE/VIEW/INDEX statement is present.</summary>
    public bool HasDropStatement { get; init; }

    /// <summary>True if a TRUNCATE statement is present.</summary>
    public bool HasTruncateStatement { get; init; }

    /// <summary>True if an ALTER TABLE statement is present.</summary>
    public bool HasAlterStatement { get; init; }
}
