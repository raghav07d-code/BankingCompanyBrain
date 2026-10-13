using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticExtractionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "DocumentVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE "DocumentVersions" AS version
                SET "WorkspaceId" = document."WorkspaceId"
                FROM "Documents" AS document
                WHERE document."Id" = version."DocumentId";
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentVersions_WorkspaceId_Id",
                table: "DocumentVersions",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateTable(
                name: "SemanticEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Aliases = table.Column<string>(type: "text", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticEntities", x => x.Id);
                    table.UniqueConstraint("AK_SemanticEntities_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "SemanticExtractionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntityCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimCount = table.Column<int>(type: "integer", nullable: false),
                    RelationshipCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticExtractionRuns", x => x.Id);
                    table.UniqueConstraint("AK_SemanticExtractionRuns_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_SemanticExtractionRuns_DocumentVersions_WorkspaceId_Documen~",
                        columns: x => new { x.WorkspaceId, x.DocumentVersionId },
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SemanticEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceLocation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    SheetName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SlideNumber = table.Column<int>(type: "integer", nullable: true),
                    RowNumber = table.Column<int>(type: "integer", nullable: true),
                    ColumnNumber = table.Column<int>(type: "integer", nullable: true),
                    CellReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    ExtractedValue = table.Column<string>(type: "text", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticEvidence", x => x.Id);
                    table.UniqueConstraint("AK_SemanticEvidence_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_SemanticEvidence_DocumentVersions_WorkspaceId_DocumentVersi~",
                        columns: x => new { x.WorkspaceId, x.DocumentVersionId },
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SemanticEvidence_SemanticExtractionRuns_WorkspaceId_Extract~",
                        columns: x => new { x.WorkspaceId, x.ExtractionRunId },
                        principalTable: "SemanticExtractionRuns",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SemanticClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObjectEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Predicate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObjectValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ExtractionConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    SourceAuthorityScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemanticClaims_DocumentVersions_WorkspaceId_DocumentVersion~",
                        columns: x => new { x.WorkspaceId, x.DocumentVersionId },
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SemanticClaims_SemanticEntities_WorkspaceId_ObjectEntityId",
                        columns: x => new { x.WorkspaceId, x.ObjectEntityId },
                        principalTable: "SemanticEntities",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemanticClaims_SemanticEntities_WorkspaceId_SubjectEntityId",
                        columns: x => new { x.WorkspaceId, x.SubjectEntityId },
                        principalTable: "SemanticEntities",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemanticClaims_SemanticEvidence_WorkspaceId_EvidenceId",
                        columns: x => new { x.WorkspaceId, x.EvidenceId },
                        principalTable: "SemanticEvidence",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SemanticRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractionConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    IdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemanticRelationships_DocumentVersions_WorkspaceId_Document~",
                        columns: x => new { x.WorkspaceId, x.DocumentVersionId },
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SemanticRelationships_SemanticEntities_WorkspaceId_ObjectEn~",
                        columns: x => new { x.WorkspaceId, x.ObjectEntityId },
                        principalTable: "SemanticEntities",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemanticRelationships_SemanticEntities_WorkspaceId_SubjectE~",
                        columns: x => new { x.WorkspaceId, x.SubjectEntityId },
                        principalTable: "SemanticEntities",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemanticRelationships_SemanticEvidence_WorkspaceId_Evidence~",
                        columns: x => new { x.WorkspaceId, x.EvidenceId },
                        principalTable: "SemanticEvidence",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_WorkspaceId_Id",
                table: "DocumentVersions",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_DocumentVersionId_IdentityHash",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "DocumentVersionId", "IdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_EvidenceId",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_ObjectEntityId",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "ObjectEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_SubjectEntityId",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "SubjectEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEntities_WorkspaceId_EntityType_NormalizedName",
                table: "SemanticEntities",
                columns: new[] { "WorkspaceId", "EntityType", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEntities_WorkspaceId_Id",
                table: "SemanticEntities",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEvidence_WorkspaceId_DocumentVersionId",
                table: "SemanticEvidence",
                columns: new[] { "WorkspaceId", "DocumentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEvidence_WorkspaceId_ExtractionRunId",
                table: "SemanticEvidence",
                columns: new[] { "WorkspaceId", "ExtractionRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEvidence_WorkspaceId_Id",
                table: "SemanticEvidence",
                columns: new[] { "WorkspaceId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticExtractionRuns_WorkspaceId_DocumentVersionId_Extrac~",
                table: "SemanticExtractionRuns",
                columns: new[] { "WorkspaceId", "DocumentVersionId", "ExtractorIdentity", "ExtractorVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticExtractionRuns_WorkspaceId_DocumentVersionId_Starte~",
                table: "SemanticExtractionRuns",
                columns: new[] { "WorkspaceId", "DocumentVersionId", "StartedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticRelationships_WorkspaceId_DocumentVersionId_Identit~",
                table: "SemanticRelationships",
                columns: new[] { "WorkspaceId", "DocumentVersionId", "IdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticRelationships_WorkspaceId_EvidenceId",
                table: "SemanticRelationships",
                columns: new[] { "WorkspaceId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticRelationships_WorkspaceId_ObjectEntityId",
                table: "SemanticRelationships",
                columns: new[] { "WorkspaceId", "ObjectEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticRelationships_WorkspaceId_SubjectEntityId",
                table: "SemanticRelationships",
                columns: new[] { "WorkspaceId", "SubjectEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticClaims");

            migrationBuilder.DropTable(
                name: "SemanticRelationships");

            migrationBuilder.DropTable(
                name: "SemanticEntities");

            migrationBuilder.DropTable(
                name: "SemanticEvidence");

            migrationBuilder.DropTable(
                name: "SemanticExtractionRuns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentVersions_WorkspaceId_Id",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_WorkspaceId_Id",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "DocumentVersions");
        }
    }
}
