using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeSourceConnectorIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectorKey",
                table: "KnowledgeSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalIdentifier",
                table: "KnowledgeSources",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectorKey",
                table: "KnowledgeSources");

            migrationBuilder.DropColumn(
                name: "ExternalIdentifier",
                table: "KnowledgeSources");
        }
    }
}
