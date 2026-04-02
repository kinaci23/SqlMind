using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for SqlMind.
/// Registers pgvector extension and maps the KnowledgeDocument / KnowledgeChunk / EmbeddingRecord aggregate.
/// </summary>
public sealed class SqlMindDbContext : DbContext
{
    public SqlMindDbContext(DbContextOptions<SqlMindDbContext> options) : base(options) { }

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk>    KnowledgeChunks    => Set<KnowledgeChunk>();
    public DbSet<EmbeddingRecord>   EmbeddingRecords   => Set<EmbeddingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── pgvector extension ────────────────────────────────────────────────
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

            // Ignore the navigation initialiser — EF owns the collection via HasMany
            e.Ignore(d => d.Chunks);
        });

        // ── KnowledgeChunk ────────────────────────────────────────────────────
        modelBuilder.Entity<KnowledgeChunk>(e =>
        {
            e.ToTable("knowledge_chunks");
            e.HasKey(c => c.Id);
            e.Property(c => c.Content).IsRequired();
            e.Property(c => c.ChunkIndex).IsRequired();

            // Metadata stored as JSON column
            e.Property(c => c.Metadata)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                 v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null)
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

            // Map float[] → pgvector vector(768)
            // The value converter serialises float[] ↔ Pgvector.Vector so the
            // Npgsql driver can store/read the vector column correctly.
            var vectorConverter = new ValueConverter<float[], Vector>(
                v => new Vector(v),
                v => v.ToArray());

            e.Property(r => r.Vector)
             .HasColumnType("vector(3072)")
             .HasConversion(vectorConverter)
             .IsRequired();

            e.HasOne(r => r.Chunk)
             .WithOne()
             .HasForeignKey<EmbeddingRecord>(r => r.ChunkId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
