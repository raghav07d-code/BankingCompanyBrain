using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeSourceRefreshRunsAndSnapshotRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSnapshotRevision",
                table: "KnowledgeSources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KnowledgeSourceRefreshRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueuedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeSourceRefreshRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeSourceRefreshRuns_WorkspaceId_KnowledgeSourceId_Ex~",
                table: "KnowledgeSourceRefreshRuns",
                columns: new[] { "WorkspaceId", "KnowledgeSourceId", "ExtractorIdentity", "QueuedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeSourceRefreshRuns");

            migrationBuilder.DropColumn(
                name: "LastSnapshotRevision",
                table: "KnowledgeSources");
        }
    }
}
