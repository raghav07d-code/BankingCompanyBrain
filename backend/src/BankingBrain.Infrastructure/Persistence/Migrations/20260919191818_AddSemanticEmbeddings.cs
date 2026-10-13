using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemanticEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    VectorJson = table.Column<string>(type: "text", nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    ProviderIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEmbeddings_WorkspaceId_DocumentVersionId",
                table: "SemanticEmbeddings",
                columns: new[] { "WorkspaceId", "DocumentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEmbeddings_WorkspaceId_SemanticClaimId",
                table: "SemanticEmbeddings",
                columns: new[] { "WorkspaceId", "SemanticClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEmbeddings_WorkspaceId_SemanticEntityId",
                table: "SemanticEmbeddings",
                columns: new[] { "WorkspaceId", "SemanticEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEmbeddings_WorkspaceId_SourceType_SourceId_Provider~",
                table: "SemanticEmbeddings",
                columns: new[] { "WorkspaceId", "SourceType", "SourceId", "ProviderIdentity", "ProviderVersion", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticEmbeddings");
        }
    }
}
