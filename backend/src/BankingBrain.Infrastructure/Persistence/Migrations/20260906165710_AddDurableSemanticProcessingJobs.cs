using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableSemanticProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemanticProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemanticProcessingJobs_DocumentVersions_WorkspaceId_Documen~",
                        columns: x => new { x.WorkspaceId, x.DocumentVersionId },
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SemanticProcessingJobs_SemanticExtractionRuns_WorkspaceId_E~",
                        columns: x => new { x.WorkspaceId, x.ExtractionRunId },
                        principalTable: "SemanticExtractionRuns",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticProcessingJobs_LeaseUntilUtc",
                table: "SemanticProcessingJobs",
                column: "LeaseUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SemanticProcessingJobs_Status_AvailableAtUtc",
                table: "SemanticProcessingJobs",
                columns: new[] { "Status", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticProcessingJobs_WorkspaceId_DocumentVersionId_Extrac~",
                table: "SemanticProcessingJobs",
                columns: new[] { "WorkspaceId", "DocumentVersionId", "ExtractorIdentity", "ExtractorVersion" },
                unique: true,
                filter: "\"Status\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_SemanticProcessingJobs_WorkspaceId_ExtractionRunId",
                table: "SemanticProcessingJobs",
                columns: new[] { "WorkspaceId", "ExtractionRunId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticProcessingJobs");
        }
    }
}
