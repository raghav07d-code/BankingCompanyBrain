using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankingSemanticTemporalModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AsOfUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "SemanticClaims",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NumericValue",
                table: "SemanticClaims",
                type: "numeric(24,8)",
                precision: 24,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodEndUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodStartUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourcePublishedOnUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "SemanticClaims",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidFromUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidToUtc",
                table: "SemanticClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueKind",
                table: "SemanticClaims",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "SemanticEntityTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticEntityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemanticPredicates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowsEntityObject = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedSubjectTypes = table.Column<string>(type: "text", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticPredicates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_Predicate_ValidFromUtc_ValidToUtc",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "Predicate", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticClaims_WorkspaceId_SubjectEntityId_Predicate_AsOfUtc",
                table: "SemanticClaims",
                columns: new[] { "WorkspaceId", "SubjectEntityId", "Predicate", "AsOfUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticEntityTypes_WorkspaceId_Code",
                table: "SemanticEntityTypes",
                columns: new[] { "WorkspaceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticPredicates_WorkspaceId_Code",
                table: "SemanticPredicates",
                columns: new[] { "WorkspaceId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticEntityTypes");

            migrationBuilder.DropTable(
                name: "SemanticPredicates");

            migrationBuilder.DropIndex(
                name: "IX_SemanticClaims_WorkspaceId_Predicate_ValidFromUtc_ValidToUtc",
                table: "SemanticClaims");

            migrationBuilder.DropIndex(
                name: "IX_SemanticClaims_WorkspaceId_SubjectEntityId_Predicate_AsOfUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "AsOfUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "NumericValue",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "PeriodEndUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "PeriodStartUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "SourcePublishedOnUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "ValidFromUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "ValidToUtc",
                table: "SemanticClaims");

            migrationBuilder.DropColumn(
                name: "ValueKind",
                table: "SemanticClaims");
        }
    }
}
