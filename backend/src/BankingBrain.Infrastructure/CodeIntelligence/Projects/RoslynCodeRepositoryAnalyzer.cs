using BankingBrain.Application.Common.CodeIntelligence;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BankingBrain.Infrastructure.CodeIntelligence.Projects;

/// <summary>
/// Builds project-aware Roslyn compilations directly from the repository's own
/// discovered project/source/reference graph and resolves C# symbols against them
/// using the Roslyn semantic model. Deliberately avoids MSBuildWorkspace: project
/// discovery (source files, project references) is already resolved by
/// <see cref="ICodeProjectDiscovery"/>/<see cref="ICodeCompilationBuilder"/>, so the
/// only Roslyn-specific work left here is compiling and semantically resolving.
/// </summary>
public sealed class RoslynCodeRepositoryAnalyzer : ICodeRepositoryAnalyzer
{
    private readonly ICodeCompilationBuilder _compilationBuilder;

    public RoslynCodeRepositoryAnalyzer(ICodeCompilationBuilder compilationBuilder)
    {
        _compilationBuilder = compilationBuilder;
    }

    public Task<CodeRepositoryAnalysisResult> AnalyzeAsync(
        CodeRepositoryStructure repository,
        IReadOnlySet<string> unchangedFilePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(unchangedFilePaths);

        var compilationPlan = _compilationBuilder.Build(repository);
        var (orderedProjects, compilations) = RoslynProjectCompiler.Build(compilationPlan, cancellationToken);

        var projectModels = new List<CodeProjectModel>();
        var projectReferenceRelationships = new List<CodeRelationship>();

        foreach (var project in orderedProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = compilations[project.ProjectFilePath];

            projectModels.Add(
                AnalyzeProject(project.ProjectName, project.ProjectFilePath, compilation, unchangedFilePaths));

            foreach (var referencePath in project.ProjectReferences)
            {
                projectReferenceRelationships.Add(
                    new CodeRelationship(
                        project.ProjectName,
                        Path.GetFileNameWithoutExtension(referencePath),
                        "REFERENCES_PROJECT",
                        project.ProjectFilePath,
                        1,
                        1));
            }
        }

        return Task.FromResult(
            new CodeRepositoryAnalysisResult(
                repository.RootPath,
                projectModels,
                projectReferenceRelationships));
    }

    private static CodeProjectModel AnalyzeProject(
        string projectName,
        string projectFilePath,
        Compilation compilation,
        IReadOnlySet<string> unchangedFilePaths)
    {
        var files = new List<CodeFileModel>();
        var symbols = new List<CodeSymbol>();
        var relationships = new List<CodeRelationship>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetCompilationUnitRoot();

            // The tree still participates fully in the compilation above (so changed
            // files elsewhere in the project resolve symbols from it correctly); only
            // the per-file symbol/relationship walk - the expensive "analysis" step
            // this pass is meant to skip for unchanged files - is skipped here.
            if (tree.FilePath is not null && unchangedFilePaths.Contains(tree.FilePath))
            {
                files.Add(new CodeFileModel(tree.FilePath, GetNamespace(root), [], []));
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree);

            var fileSymbols = ExtractSymbols(semanticModel, root);
            var fileRelationships = ExtractRelationships(semanticModel, root);

            symbols.AddRange(fileSymbols);
            relationships.AddRange(fileRelationships);

            files.Add(
                new CodeFileModel(
                    tree.FilePath ?? string.Empty,
                    GetNamespace(root),
                    fileSymbols,
                    []));
        }

        return new CodeProjectModel(
            projectName,
            projectFilePath,
            files,
            symbols,
            relationships);
    }

    private static IReadOnlyList<CodeSymbol> ExtractSymbols(
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        var symbols = new List<CodeSymbol>();

        foreach (var namespaceDeclaration in root
                     .DescendantNodes()
                     .OfType<BaseNamespaceDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(namespaceDeclaration);

            if (symbol is not null)
            {
                symbols.Add(
                    CreateSymbol(symbol, CodeSymbolKind.Namespace, namespaceDeclaration));
            }
        }

        foreach (var typeDeclaration in root
                     .DescendantNodes()
                     .OfType<BaseTypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            symbols.Add(
                CreateSymbol(typeSymbol, GetTypeKind(typeSymbol), typeDeclaration));

            if (typeDeclaration is not TypeDeclarationSyntax typeMembers)
            {
                continue;
            }

            foreach (var member in typeMembers.Members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var fieldSymbol = semanticModel.GetDeclaredSymbol(variable);

                        if (fieldSymbol is not null)
                        {
                            symbols.Add(
                                CreateSymbol(fieldSymbol, CodeSymbolKind.Field, variable));
                        }
                    }

                    continue;
                }

                var memberSymbol = semanticModel.GetDeclaredSymbol(member);

                if (memberSymbol is null)
                {
                    continue;
                }

                var kind = member switch
                {
                    MethodDeclarationSyntax => CodeSymbolKind.Method,
                    ConstructorDeclarationSyntax => CodeSymbolKind.Method,
                    PropertyDeclarationSyntax => CodeSymbolKind.Property,
                    _ => (CodeSymbolKind?)null
                };

                if (kind is not null)
                {
                    symbols.Add(CreateSymbol(memberSymbol, kind.Value, member));
                }
            }
        }

        return symbols;
    }

    private static IReadOnlyList<CodeRelationship> ExtractRelationships(
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        var relationships = new List<CodeRelationship>();

        foreach (var typeDeclaration in root
                     .DescendantNodes()
                     .OfType<TypeDeclarationSyntax>())
        {
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);

            if (typeSymbol is null)
            {
                continue;
            }

            var source = GetSymbolName(typeSymbol);

            if (typeSymbol.BaseType is not null &&
                typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                AddRelationship(
                    relationships,
                    source,
                    typeSymbol.BaseType,
                    "INHERITS",
                    typeDeclaration);
            }

            foreach (var interfaceSymbol in typeSymbol.Interfaces)
            {
                AddRelationship(
                    relationships,
                    source,
                    interfaceSymbol,
                    "IMPLEMENTS",
                    typeDeclaration);
            }

            var typeDependencies = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var member in typeDeclaration.Members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol fieldSymbol)
                        {
                            CollectRepositoryDeclaredTypes(fieldSymbol.Type, typeDependencies);
                        }
                    }
                }
                else if (member is PropertyDeclarationSyntax property &&
                         semanticModel.GetDeclaredSymbol(property) is IPropertySymbol propertySymbol)
                {
                    CollectRepositoryDeclaredTypes(propertySymbol.Type, typeDependencies);
                }
            }

            foreach (var dependency in typeDependencies)
            {
                AddRelationship(
                    relationships,
                    source,
                    dependency,
                    "TYPE_DEPENDS_ON_TYPE",
                    typeDeclaration);
            }
        }

        foreach (var methodDeclaration in root
                     .DescendantNodes()
                     .OfType<MethodDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(methodDeclaration) is IMethodSymbol methodSymbol)
            {
                AddMethodTypeDependencies(relationships, methodSymbol, methodDeclaration);
            }
        }

        foreach (var constructorDeclaration in root
                     .DescendantNodes()
                     .OfType<ConstructorDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(constructorDeclaration) is IMethodSymbol constructorSymbol)
            {
                AddMethodTypeDependencies(relationships, constructorSymbol, constructorDeclaration);
            }
        }

        foreach (var invocation in root
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            var source = semanticModel.GetEnclosingSymbol(invocation.SpanStart);
            var target = semanticModel.GetSymbolInfo(invocation).Symbol;

            if (source is null ||
                target is null ||
                source.Kind != SymbolKind.Method ||
                target.Kind != SymbolKind.Method)
            {
                continue;
            }

            AddRelationship(
                relationships,
                GetSymbolName(source),
                target,
                "CALLS",
                invocation);
        }

        return relationships;
    }

    private static void AddMethodTypeDependencies(
        List<CodeRelationship> relationships,
        IMethodSymbol methodSymbol,
        SyntaxNode methodDeclaration)
    {
        var dependencies = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (!methodSymbol.ReturnsVoid)
        {
            CollectRepositoryDeclaredTypes(methodSymbol.ReturnType, dependencies);
        }

        foreach (var parameter in methodSymbol.Parameters)
        {
            CollectRepositoryDeclaredTypes(parameter.Type, dependencies);
        }

        var source = GetSymbolName(methodSymbol);

        foreach (var dependency in dependencies)
        {
            AddRelationship(
                relationships,
                source,
                dependency,
                "METHOD_DEPENDS_ON_TYPE",
                methodDeclaration);
        }
    }

    /// <summary>
    /// Walks a type (including array elements and generic type arguments) purely through
    /// Roslyn symbol APIs, collecting named types actually declared in one of the analyzed
    /// repository's own compilations. Cross-project references resolve through
    /// CompilationReference (see CreateCompilation), so repo-declared types remain
    /// IsInSource even from a dependent project; BCL/NuGet types loaded as raw metadata
    /// never are, so they are excluded without any string/identifier matching.
    /// </summary>
    private static void CollectRepositoryDeclaredTypes(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> result)
    {
        switch (type)
        {
            case null:
                return;

            case IArrayTypeSymbol arrayType:
                CollectRepositoryDeclaredTypes(arrayType.ElementType, result);
                return;

            case INamedTypeSymbol namedType:
                if (IsDeclaredInSource(namedType))
                {
                    result.Add(namedType);
                }

                foreach (var typeArgument in namedType.TypeArguments)
                {
                    CollectRepositoryDeclaredTypes(typeArgument, result);
                }

                return;
        }
    }

    private static bool IsDeclaredInSource(ISymbol symbol)
    {
        return symbol.Locations.Any(location => location.IsInSource);
    }

    private static void AddRelationship(
        List<CodeRelationship> relationships,
        string source,
        ISymbol target,
        string kind,
        SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();

        relationships.Add(
            new CodeRelationship(
                source,
                GetSymbolName(target),
                kind,
                span.Path ?? string.Empty,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1));
    }

    private static CodeSymbol CreateSymbol(
        ISymbol symbol,
        CodeSymbolKind kind,
        SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();

        return new CodeSymbol(
            symbol.Name,
            kind,
            GetSymbolName(symbol),
            symbol.ContainingSymbol is null
                ? null
                : GetSymbolName(symbol.ContainingSymbol),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1);
    }

    private static string GetSymbolName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static CodeSymbolKind GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord)
        {
            return CodeSymbolKind.Record;
        }

        return symbol.TypeKind switch
        {
            TypeKind.Interface => CodeSymbolKind.Interface,
            TypeKind.Struct => CodeSymbolKind.Struct,
            TypeKind.Enum => CodeSymbolKind.Enum,
            _ => CodeSymbolKind.Class
        };
    }

    private static string? GetNamespace(CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(x => x.Name.ToString())
            .FirstOrDefault();
    }
}
