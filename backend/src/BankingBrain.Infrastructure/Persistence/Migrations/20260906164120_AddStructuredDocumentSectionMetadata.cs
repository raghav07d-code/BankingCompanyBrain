using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredDocumentSectionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CellReference",
                table: "DocumentSections",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColumnNumber",
                table: "DocumentSections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "DocumentSections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentSequence",
                table: "DocumentSections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowNumber",
                table: "DocumentSections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SheetName",
                table: "DocumentSections",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlideNumber",
                table: "DocumentSections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "DocumentSections",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CellReference",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "ColumnNumber",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "ParentSequence",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "RowNumber",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "SheetName",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "SlideNumber",
                table: "DocumentSections");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "DocumentSections");
        }
    }
}
