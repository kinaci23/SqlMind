using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.Persistence;

namespace SqlMind.Infrastructure.Schema;

/// <summary>
/// ISchemaIngestionService implementation.
///
/// Connects directly to the target PostgreSQL database via Npgsql,
/// queries information_schema + pg_catalog for rich schema metadata,
/// formats each table as a KnowledgeDocument, and indexes it through IRagService.
/// </summary>
public sealed class SchemaIngestionService : ISchemaIngestionService
{
    private readonly IRagService _ragService;
    private readonly SqlMindDbContext _db;
    private readonly ILogger<SchemaIngestionService> _logger;

    public SchemaIngestionService(
        IRagService ragService,
        SqlMindDbContext db,
        ILogger<SchemaIngestionService> logger)
    {
        _ragService = ragService;
        _db = db;
        _logger = logger;
    }

    // ── ISchemaIngestionService ───────────────────────────────────────────────

    public async Task<SchemaIngestionResult> IngestAsync(
        string connectionString,
        string environment,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Schema ingestion started — environment={Environment}", environment);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var tables = await GetTableNamesAsync(conn, ct);
        _logger.LogInformation("Found {Count} tables to ingest", tables.Count);

        int documentsCreated = 0;

        foreach (var tableName in tables)
        {
            ct.ThrowIfCancellationRequested();

            var columns   = await GetColumnsAsync(conn, tableName, ct);
            var rowCount  = await GetRowCountEstimateAsync(conn, tableName, ct);
            var indexes   = await GetIndexesAsync(conn, tableName, ct);
            var outgoingFks = await GetOutgoingForeignKeysAsync(conn, tableName, ct);
            var incomingFks = await GetIncomingForeignKeysAsync(conn, tableName, ct);

            var doc = BuildDocument(tableName, environment, rowCount, columns, indexes, outgoingFks, incomingFks);

            await _ragService.IndexDocumentAsync(doc, ct);
            documentsCreated++;

            _logger.LogDebug("Ingested table {TableName}", tableName);
        }

        _logger.LogInformation(
            "Schema ingestion complete — {Count} documents created", documentsCreated);

        return new SchemaIngestionResult
        {
            Environment      = environment,
            TablesIngested   = tables,
            DocumentsCreated = documentsCreated,
            IngestedAt       = DateTime.UtcNow,
        };
    }

    public async Task<List<string>> GetIngestedTablesAsync(
        string environment,
        CancellationToken ct = default)
    {
        var prefix = $"schema-ingestion:{environment}:";

        var titles = await _db.KnowledgeDocuments
            .Where(d => d.Source == "schema-ingestion" && d.Title.Contains(environment))
            .Select(d => d.Title)
            .ToListAsync(ct);

        // Extract table name from title: "{tableName} tablosu — otomatik şema ({environment})"
        return titles
            .Select(t =>
            {
                var idx = t.IndexOf(" tablosu", StringComparison.Ordinal);
                return idx > 0 ? t[..idx] : t;
            })
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<List<string>> GetTableNamesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type   = 'BASE TABLE'
            ORDER BY table_name
            """;

        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<string>();
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));

        return result;
    }

    private static async Task<List<ColumnInfo>> GetColumnsAsync(
        NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        const string sql =
            """
            SELECT column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name   = @tableName
            ORDER BY ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<ColumnInfo>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ColumnInfo(
                Name:       reader.GetString(0),
                DataType:   reader.GetString(1),
                IsNullable: reader.GetString(2) == "YES",
                Default:    reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    private static async Task<long> GetRowCountEstimateAsync(
        NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        const string sql =
            "SELECT reltuples::bigint FROM pg_class WHERE relname = @tableName";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : (result is int i ? i : 0L);
    }

    private static async Task<List<IndexInfo>> GetIndexesAsync(
        NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        const string sql =
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE tablename = @tableName
            ORDER BY indexname
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<IndexInfo>();
        while (await reader.ReadAsync(ct))
            result.Add(new IndexInfo(reader.GetString(0), reader.GetString(1)));

        return result;
    }

    private static async Task<List<ForeignKeyInfo>> GetOutgoingForeignKeysAsync(
        NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        // Foreign keys that go OUT from @tableName to another table.
        const string sql =
            """
            SELECT kcu.column_name,
                   ccu.table_name  AS foreign_table,
                   ccu.column_name AS foreign_column
            FROM information_schema.table_constraints       tc
            JOIN information_schema.key_column_usage        kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema    = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON tc.constraint_name = ccu.constraint_name
             AND tc.table_schema    = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_name      = @tableName
            ORDER BY kcu.column_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<ForeignKeyInfo>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ForeignKeyInfo(
                ColumnName:    reader.GetString(0),
                ForeignTable:  reader.GetString(1),
                ForeignColumn: reader.GetString(2)));
        }

        return result;
    }

    private static async Task<List<IncomingForeignKeyInfo>> GetIncomingForeignKeysAsync(
        NpgsqlConnection conn, string tableName, CancellationToken ct)
    {
        // Foreign keys that point IN to @tableName from other tables.
        const string sql =
            """
            SELECT tc.table_name          AS source_table,
                   kcu.column_name        AS source_column,
                   ccu.column_name        AS target_column
            FROM information_schema.table_constraints       tc
            JOIN information_schema.key_column_usage        kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema    = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON tc.constraint_name = ccu.constraint_name
             AND tc.table_schema    = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND ccu.table_name     = @tableName
            ORDER BY tc.table_name, kcu.column_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<IncomingForeignKeyInfo>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new IncomingForeignKeyInfo(
                SourceTable:  reader.GetString(0),
                SourceColumn: reader.GetString(1),
                TargetColumn: reader.GetString(2)));
        }

        return result;
    }

    // ── Document builder ──────────────────────────────────────────────────────

    internal static KnowledgeDocument BuildDocument(
        string tableName,
        string environment,
        long rowCount,
        List<ColumnInfo> columns,
        List<IndexInfo> indexes,
        List<ForeignKeyInfo> outgoingFks,
        List<IncomingForeignKeyInfo> incomingFks)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Tablo: {tableName}");
        sb.AppendLine($"Ortam: {environment}");
        sb.AppendLine($"Tahmini satır sayısı: {rowCount}");
        sb.AppendLine();

        // ── Columns ───────────────────────────────────────────────────────────
        sb.AppendLine("Kolonlar:");
        foreach (var col in columns)
        {
            var nullable = col.IsNullable ? "nullable" : "not null";
            var defPart  = col.Default is not null ? $", varsayılan: {col.Default}" : string.Empty;
            sb.AppendLine($"- {col.Name} ({col.DataType}, {nullable}{defPart})");
        }

        // ── Indexes ───────────────────────────────────────────────────────────
        if (indexes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Index'ler:");
            foreach (var idx in indexes)
                sb.AppendLine($"- {idx.IndexName}: {idx.IndexDef}");
        }

        // ── Outgoing FKs ──────────────────────────────────────────────────────
        if (outgoingFks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Bu tablodan çıkan foreign key'ler (bu tablo başka tablolara bağlı):");
            foreach (var fk in outgoingFks)
                sb.AppendLine($"- {fk.ColumnName} → {fk.ForeignTable}.{fk.ForeignColumn}");
        }

        // ── Incoming FKs ─────────────────────────────────────────────────────
        if (incomingFks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Bu tabloya gelen foreign key'ler (başka tablolar buraya bağlı):");
            foreach (var fk in incomingFks)
                sb.AppendLine($"- {fk.SourceTable}.{fk.SourceColumn} → {tableName}.{fk.TargetColumn}");
        }

        return new KnowledgeDocument
        {
            Title   = $"{tableName} tablosu — otomatik şema ({environment})",
            Source  = "schema-ingestion",
            Content = sb.ToString().TrimEnd(),
        };
    }

    // ── Private record types ──────────────────────────────────────────────────

    internal sealed record ColumnInfo(string Name, string DataType, bool IsNullable, string? Default);
    internal sealed record IndexInfo(string IndexName, string IndexDef);
    internal sealed record ForeignKeyInfo(string ColumnName, string ForeignTable, string ForeignColumn);
    internal sealed record IncomingForeignKeyInfo(string SourceTable, string SourceColumn, string TargetColumn);
}
