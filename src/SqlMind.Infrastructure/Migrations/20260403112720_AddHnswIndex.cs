using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlMind.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHnswIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pgvector HNSW/IVFFlat support max 2000 dims for vector type.
            // Gemini text-embedding-004 produces 3072-dim vectors, so we cast to halfvec
            // inline (supported in pgvector >= 0.7.0) to stay within the limit.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_embeddings_vector_hnsw " +
                "ON embeddings USING hnsw ((\"Vector\"::halfvec(3072)) halfvec_cosine_ops) " +
                "WITH (m = 16, ef_construction = 64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS idx_embeddings_vector_hnsw");
        }
    }
}
