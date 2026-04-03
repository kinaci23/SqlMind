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
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_embeddings_vector_hnsw " +
                "ON embeddings USING hnsw (vector vector_cosine_ops) " +
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
