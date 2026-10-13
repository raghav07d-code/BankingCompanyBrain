using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenSemanticEntityIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SemanticEntities_WorkspaceId_EntityType_ExternalIdentifier",
                table: "SemanticEntities",
                columns: new[] { "WorkspaceId", "EntityType", "ExternalIdentifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SemanticEntities_WorkspaceId_EntityType_ExternalIdentifier",
                table: "SemanticEntities");
        }
    }
}
