using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIngestionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DocumentVersions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "DocumentVersions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "DocumentVersions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingCompletedOnUtc",
                table: "DocumentVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "DocumentVersions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedOnUtc",
                table: "DocumentVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessorVersion",
                table: "DocumentVersions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "DocumentVersions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentVersionId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    SourceLocation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSections_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProcessorVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingAttempts_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvenanceAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    SheetName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SectionReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtractedValue = table.Column<string>(type: "text", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvenanceAnchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvenanceAnchors_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_ContentHash",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSections_DocumentVersionId_Sequence",
                table: "DocumentSections",
                columns: new[] { "DocumentVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingAttempts_DocumentVersionId_AttemptNumber",
                table: "ProcessingAttempts",
                columns: new[] { "DocumentVersionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvenanceAnchors_DocumentVersionId",
                table: "ProvenanceAnchors",
                column: "DocumentVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DocumentVersions_CurrentVersionId",
                table: "Documents",
                column: "CurrentVersionId",
                principalTable: "DocumentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DocumentVersions_CurrentVersionId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentSections");

            migrationBuilder.DropTable(
                name: "ProcessingAttempts");

            migrationBuilder.DropTable(
                name: "ProvenanceAnchors");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_DocumentId_ContentHash",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_Documents_CurrentVersionId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "ProcessingCompletedOnUtc",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedOnUtc",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "ProcessorVersion",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                table: "Documents");
        }
    }
}
