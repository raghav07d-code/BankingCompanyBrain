using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.DataAccessIntelligence;
using BankingBrain.Infrastructure.CodeIntelligence.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BankingBrain.Infrastructure.DataAccessIntelligence;

/// <summary>
/// Finds EF Core <c>DbSet&lt;TEntity&gt;</c> accesses and resolves each one's table name
/// only through an explicit <c>IEntityTypeConfiguration&lt;TEntity&gt;.ToTable("Name")</c>
/// declaration elsewhere in the same repository - never through EF's naming convention
/// (pluralized class name), since that would be a guess, not a fact read from source.
/// Built on the same shared <see cref="RoslynProjectCompiler"/> Code Intelligence and API
/// Intelligence both use, so all three see the exact same compiled repository.
/// </summary>
public sealed class EfDataAccessAnalyzer : IEfDataAccessAnalyzer
{
    private readonly ICodeCompilationBuilder _compilationBuilder;

    public EfDataAccessAnalyzer(ICodeCompilationBuilder compilationBuilder)
    {
        _compilationBuilder = compilationBuilder;
    }

    public IReadOnlyList<EfTableAccess> Analyze(CodeRepositoryStructure repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var compilationPlan = _compilationBuilder.Build(repository);
        var (_, compilations) = RoslynProjectCompiler.Build(compilationPlan);

        // Pass 1: index every explicit EF Core table mapping, by the FQN of the entity
        // CLR type it configures, across every project's own compilation.
        var tableNameByEntityFqn = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var compilation in compilations.Values)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);

                foreach (var classDeclaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                        continue;

                    var configurationInterface = classSymbol.AllInterfaces
                        .FirstOrDefault(i => i.Name == "IEntityTypeConfiguration" && i.TypeArguments.Length == 1);

                    if (configurationInterface is null)
                        continue;

                    var entityFqn = GetSymbolName(configurationInterface.TypeArguments[0]);

                    foreach (var invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                            memberAccess.Name.Identifier.Text != "ToTable" ||
                            invocation.ArgumentList.Arguments.Count == 0)
                            continue;

                        if (invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                            literal.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            tableNameByEntityFqn[entityFqn] = literal.Token.ValueText;
                        }
                    }
                }
            }
        }

        // Pass 2: find every method that accesses a DbSet<TEntity> property declared on a
        // DbContext-derived class, and resolve its table name through the index above.
        var accesses = new List<EfTableAccess>();

        foreach (var compilation in compilations.Values)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.FilePath is null)
                    continue;

                var semanticModel = compilation.GetSemanticModel(tree);

                foreach (var nameNode in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (semanticModel.GetSymbolInfo(nameNode).Symbol is not IPropertySymbol propertySymbol)
                        continue;

                    if (propertySymbol.Type is not INamedTypeSymbol { Name: "DbSet", TypeArguments.Length: 1 } dbSetType)
                        continue;

                    if (!IsDbContext(propertySymbol.ContainingType))
                        continue;

                    var enclosing = semanticModel.GetEnclosingSymbol(nameNode.SpanStart);
                    if (enclosing is not IMethodSymbol { } enclosingMethod)
                        continue;

                    var entityFqn = GetSymbolName(dbSetType.TypeArguments[0]);
                    var line = nameNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    accesses.Add(new EfTableAccess(
                        GetSymbolName(enclosingMethod),
                        entityFqn,
                        tableNameByEntityFqn.TryGetValue(entityFqn, out var tableName) ? tableName : null,
                        tree.FilePath,
                        line));
                }
            }
        }

        return accesses
            .DistinctBy(access => (access.MethodFqn, access.EntityTypeFqn, access.Line))
            .ToArray();
    }

    private static bool IsDbContext(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext")
                return true;
        }
        return false;
    }

    private static string GetSymbolName(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
