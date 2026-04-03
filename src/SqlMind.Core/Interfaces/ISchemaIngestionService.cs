using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Connects to a target database, reads its schema via information_schema,
/// and indexes each table as a KnowledgeDocument so RAG can surface real
/// schema context when analysing SQL.
/// </summary>
public interface ISchemaIngestionService
{
    /// <summary>
    /// Reads every table in the public schema of the given database,
    /// builds a rich KnowledgeDocument per table (columns, indexes, FK relations,
    /// estimated row count) and indexes them through IRagService.
    /// </summary>
    /// <param name="connectionString">Npgsql-compatible connection string for the target DB.</param>
    /// <param name="environment">Logical environment tag stored in the document (e.g. "production").</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SchemaIngestionResult> IngestAsync(
        string connectionString,
        string environment,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the list of table names that have already been ingested
    /// for the given environment (sourced from knowledge_documents).
    /// </summary>
    Task<List<string>> GetIngestedTablesAsync(string environment, CancellationToken ct = default);
}
