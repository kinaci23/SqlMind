using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using SqlMind.Core.Models;
using System.Text.Json;

namespace SqlMind.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for SqlMind.
/// Registers pgvector extension and maps all domain aggregates to their tables.
/// </summary>
public sealed class SqlMindDbContext : DbContext
{
    public SqlMindDbContext(DbContextOptions<SqlMindDbContext> options) : base(options) { }

    // ── RAG pipeline ──────────────────────────────────────────────────────────
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk>    KnowledgeChunks    => Set<KnowledgeChunk>();
    public DbSet<EmbeddingRecord>   EmbeddingRecords   => Set<EmbeddingRecord>();

    // ── Analysis pipeline ─────────────────────────────────────────────────────
    public DbSet<AnalysisJob>    AnalysisJobs    => Set<AnalysisJob>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();
    public DbSet<AuditLog>       AuditLogs       => Set<AuditLog>();

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── pgvector extension ────────────────────────────────────────────────
        // Guard: InMemory provider (used in unit tests) does not support Npgsql extensions.
        if (Database.IsNpgsql())
            modelBuilder.HasPostgresExtension("vector");

        // ── KnowledgeDocument ─────────────────────────────────────────────────
        modelBuilder.Entity<KnowledgeDocument>(e =>
        {
            e.ToTable("knowledge_documents");
            e.HasKey(d => d.Id);
            e.Property(d => d.Title).HasMaxLength(500).IsRequired();
            e.Property(d => d.Content).IsRequired();
            e.Property(d => d.Source).HasMaxLength(1000);
            e.Property(d => d.CreatedAt).IsRequired();
            e.Property(d => d.Version).IsRequired().HasDefaultValue(1);
            e.Ignore(d => d.Chunks);
        });

        // ── KnowledgeChunk ────────────────────────────────────────────────────
        modelBuilder.Entity<KnowledgeChunk>(e =>
        {
            e.ToTable("knowledge_chunks");
            e.HasKey(c => c.Id);
            e.Property(c => c.Content).IsRequired();
            e.Property(c => c.ChunkIndex).IsRequired();

            e.Property(c => c.Metadata)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, _jsonOpts),
                 v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, _jsonOpts)
                      ?? new Dictionary<string, string>());

            e.HasOne(c => c.Document)
             .WithMany()
             .HasForeignKey(c => c.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(c => c.Embedding);
        });

        // ── EmbeddingRecord ───────────────────────────────────────────────────
        modelBuilder.Entity<EmbeddingRecord>(e =>
        {
            e.ToTable("embeddings");
            e.HasKey(r => r.Id);
            e.Property(r => r.CreatedAt).IsRequired();

            // Npgsql-only: pgvector type requires Npgsql type mappings (UseVector()).
            // InMemory provider (unit tests) ignores this block and stores float[] natively.
            if (Database.IsNpgsql())
            {
                var vectorConverter = new ValueConverter<float[], Vector>(
                    v => new Vector(v),
                    v => v.ToArray());

                e.Property(r => r.Vector)
                 .HasColumnType("vector(3072)")
                 .HasConversion(vectorConverter)
                 .IsRequired();
            }
            else
            {
                e.Property(r => r.Vector).IsRequired();
            }

            e.HasOne(r => r.Chunk)
             .WithOne()
             .HasForeignKey<EmbeddingRecord>(r => r.ChunkId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AnalysisJob ───────────────────────────────────────────────────────
        modelBuilder.Entity<AnalysisJob>(e =>
        {
            e.ToTable("analysis_jobs");
            e.HasKey(j => j.Id);
            e.Property(j => j.CorrelationId).HasMaxLength(100).IsRequired();
            e.Property(j => j.SqlContent).IsRequired();
            e.Property(j => j.InputHash).HasMaxLength(64).IsRequired();
            e.Property(j => j.Status).HasMaxLength(20).IsRequired();
            e.Property(j => j.BackgroundJobId).HasMaxLength(100);
            e.Property(j => j.ParseResultJson).HasColumnType("jsonb");
            e.Property(j => j.CreatedAt).IsRequired();
            e.HasIndex(j => j.CorrelationId);
            e.HasIndex(j => j.InputHash);
        });

        // ── AnalysisResult ────────────────────────────────────────────────────
        modelBuilder.Entity<AnalysisResult>(e =>
        {
            e.ToTable("analysis_results");
            e.HasKey(r => r.Id);
            e.Property(r => r.CorrelationId).HasMaxLength(100).IsRequired();
            e.Property(r => r.LlmOutput).IsRequired();
            e.Property(r => r.AggregateRiskLevel).IsRequired();
            e.Property(r => r.RagUsed).IsRequired();
            e.Property(r => r.CreatedAt).IsRequired();

            // Store Findings as JSONB
            e.Property(r => r.Findings)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, _jsonOpts),
                 v => (IReadOnlyList<RiskFinding>)(JsonSerializer.Deserialize<List<RiskFinding>>(v, _jsonOpts)
                      ?? new List<RiskFinding>()));

            // Store ExecutedTools as JSONB
            e.Property(r => r.ExecutedTools)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, _jsonOpts),
                 v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, _jsonOpts)
                      ?? new List<string>()));

            e.HasIndex(r => r.CorrelationId);
            e.HasIndex(r => r.JobId);
        });

        // ── AuditLog ──────────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(a => a.Id);
            e.Property(a => a.CorrelationId).HasMaxLength(100).IsRequired();
            e.Property(a => a.InputHash).HasMaxLength(64).IsRequired();
            e.Property(a => a.SqlParseResult).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.RuleTriggers).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.LlmOutput).IsRequired();
            e.Property(a => a.RagUsed).IsRequired();
            e.Property(a => a.ToolExecution).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.Timestamp).IsRequired();
            e.HasIndex(a => a.CorrelationId);
            e.HasIndex(a => a.InputHash);
        });
    }
}
