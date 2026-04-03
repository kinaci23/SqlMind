using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlMind.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SqlContent = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BackgroundJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "analysis_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LlmOutput = table.Column<string>(type: "text", nullable: false),
                    AggregateRiskLevel = table.Column<int>(type: "integer", nullable: false),
                    Findings = table.Column<string>(type: "jsonb", nullable: false),
                    RagUsed = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutedTools = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SqlParseResult = table.Column<string>(type: "jsonb", nullable: false),
                    RuleTriggers = table.Column<string>(type: "jsonb", nullable: false),
                    LlmOutput = table.Column<string>(type: "text", nullable: false),
                    RagUsed = table.Column<bool>(type: "boolean", nullable: false),
                    ToolExecution = table.Column<string>(type: "jsonb", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_jobs_CorrelationId",
                table: "analysis_jobs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_jobs_InputHash",
                table: "analysis_jobs",
                column: "InputHash");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_results_CorrelationId",
                table: "analysis_results",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_results_JobId",
                table: "analysis_results",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CorrelationId",
                table: "audit_logs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_InputHash",
                table: "audit_logs",
                column: "InputHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_jobs");

            migrationBuilder.DropTable(
                name: "analysis_results");

            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
