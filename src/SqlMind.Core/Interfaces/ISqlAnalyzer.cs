using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Parses SQL scripts into an AST-like structure, extracting operations, tables,
/// and preliminary risk signals. This is the FIRST layer in the analysis pipeline.
/// Regex alone is insufficient — full tokenization/AST analysis is required.
/// </summary>
public interface ISqlAnalyzer
{
    /// <summary>
    /// Parses a SQL script and returns a structured parse result.
    /// </summary>
    /// <param name="sql">Raw SQL script to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured parse result including operations, tables, and flags.</returns>
    Task<SqlParseResult> ParseAsync(string sql, CancellationToken cancellationToken = default);
}
