namespace SqlMind.Core.Models;

/// <summary>
/// Summary returned after a schema ingestion run.
/// </summary>
public sealed class SchemaIngestionResult
{
    /// <summary>Logical environment tag supplied by the caller (e.g. "production").</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>Names of tables that were discovered and ingested.</summary>
    public List<string> TablesIngested { get; init; } = [];

    /// <summary>Number of KnowledgeDocuments written to the knowledge base.</summary>
    public int DocumentsCreated { get; init; }

    /// <summary>UTC timestamp of when the ingestion completed.</summary>
    public DateTime IngestedAt { get; init; } = DateTime.UtcNow;
}
