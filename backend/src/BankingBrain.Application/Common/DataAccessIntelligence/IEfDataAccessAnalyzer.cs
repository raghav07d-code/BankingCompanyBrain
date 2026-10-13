using BankingBrain.Application.Common.CodeIntelligence.Projects;

namespace BankingBrain.Application.Common.DataAccessIntelligence;

public interface IEfDataAccessAnalyzer
{
    /// <summary>
    /// Finds every repository-declared method that statically accesses an EF Core
    /// <c>DbSet&lt;TEntity&gt;</c> whose table is explicitly declared via
    /// <c>IEntityTypeConfiguration&lt;TEntity&gt;.Configure(...).ToTable("Name")</c> - the
    /// narrowest EF Core pattern that names a SQL table as a literal, type-checked fact
    /// rather than a naming-convention guess. A DbSet access whose entity type has no such
    /// explicit configuration is reported with <see cref="EfTableAccess.TableName"/> null
    /// (not inferred from the convention default), so the caller can mark it unresolved
    /// instead of fabricating a table name.
    /// </summary>
    IReadOnlyList<EfTableAccess> Analyze(CodeRepositoryStructure repository);
}

public sealed record EfTableAccess(
    string MethodFqn,
    string EntityTypeFqn,
    string? TableName,
    string AbsoluteFilePath,
    int Line);
