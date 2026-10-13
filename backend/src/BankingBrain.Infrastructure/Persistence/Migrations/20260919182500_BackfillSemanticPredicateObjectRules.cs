using Microsoft.EntityFrameworkCore.Migrations;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace BankingBrain.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260919182500_BackfillSemanticPredicateObjectRules")]
public partial class BackfillSemanticPredicateObjectRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'ACCOUNT\\nLOAN_FACILITY\\nSECURITY\\nPORTFOLIO\\nFUND\\nORGANIZATION\\nINSTITUTION' WHERE \"Code\" = 'OWNS'");
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'ORGANIZATION\\nPERSON\\nFUND' WHERE \"Code\" = 'LENDS_TO'");
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'INSTITUTION\\nORGANIZATION' WHERE \"Code\" = 'BORROWS_FROM'");
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'SECURITY\\nACCOUNT\\nPROPERTY\\nLOAN_FACILITY' WHERE \"Code\" = 'HAS_ASSET'");
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'LOAN_FACILITY\\nACCOUNT' WHERE \"Code\" = 'HAS_LIABILITY'");
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = 'INSTITUTION\\nORGANIZATION\\nPERSON' WHERE \"Code\" = 'HAS_RELATIONSHIP'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"SemanticPredicates\" SET \"AllowedObjectTypes\" = ''");
    }
}
