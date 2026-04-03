using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlMind.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParseResultJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParseResultJson",
                table: "analysis_jobs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParseResultJson",
                table: "analysis_jobs");
        }
    }
}
