using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticGraphProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemanticGraphEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Predicate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceRelationshipId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupportingClaimIdsJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "text", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AsOfUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtractionConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    SourceAuthorityScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false),
                    IdentityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticGraphEdges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemanticGraphNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticGraphNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticGraphEdges_WorkspaceId_AsOfUtc_ValidFromUtc_ValidTo~",
                table: "SemanticGraphEdges",
                columns: new[] { "WorkspaceId", "AsOfUtc", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticGraphEdges_WorkspaceId_IdentityHash",
                table: "SemanticGraphEdges",
                columns: new[] { "WorkspaceId", "IdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticGraphEdges_WorkspaceId_ObjectEntityId_Predicate",
                table: "SemanticGraphEdges",
                columns: new[] { "WorkspaceId", "ObjectEntityId", "Predicate" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticGraphEdges_WorkspaceId_SubjectEntityId_Predicate",
                table: "SemanticGraphEdges",
                columns: new[] { "WorkspaceId", "SubjectEntityId", "Predicate" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticGraphNodes_WorkspaceId_EntityId",
                table: "SemanticGraphNodes",
                columns: new[] { "WorkspaceId", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticGraphEdges");

            migrationBuilder.DropTable(
                name: "SemanticGraphNodes");
        }
    }
}
