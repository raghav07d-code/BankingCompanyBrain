using BankingBrain.Application.Common.CodeIntelligence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BankingBrain.Infrastructure.CodeIntelligence;

public sealed class RoslynCSharpCodeParser : ICSharpCodeParser
{
    public CodeFileModel Parse(string filePath, string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var symbols = new List<CodeSymbol>();
        var relationships = new List<CodeTypeRelationship>();

        foreach (var namespaceDeclaration in root.DescendantNodes()
                     .OfType<BaseNamespaceDeclarationSyntax>())
        {
            var namespaceName = namespaceDeclaration.Name.ToString();

            symbols.Add(CreateSymbol(
                namespaceName,
                CodeSymbolKind.Namespace,
                namespaceName,
                null,
                namespaceDeclaration));

            foreach (var typeDeclaration in namespaceDeclaration.DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                ExtractType(
                    typeDeclaration,
                    namespaceName,
                    symbols,
                    relationships);
            }
        }

        foreach (var typeDeclaration in root.DescendantNodes()
                     .OfType<TypeDeclarationSyntax>()
                     .Where(x => x.Parent is CompilationUnitSyntax))
        {
            ExtractType(
                typeDeclaration,
                null,
                symbols,
                relationships);
        }

        return new CodeFileModel(
            filePath,
            symbols.FirstOrDefault(x => x.Kind == CodeSymbolKind.Namespace)?.Name,
            symbols,
            relationships);
    }

    private static void ExtractType(
        TypeDeclarationSyntax typeDeclaration,
        string? namespaceName,
        List<CodeSymbol> symbols,
        List<CodeTypeRelationship> relationships)
    {
        var kind = typeDeclaration switch
        {
            InterfaceDeclarationSyntax => CodeSymbolKind.Interface,
            StructDeclarationSyntax => CodeSymbolKind.Struct,
            RecordDeclarationSyntax => CodeSymbolKind.Record,
            _ => CodeSymbolKind.Class
        };

        var fullyQualifiedName = string.IsNullOrWhiteSpace(namespaceName)
            ? typeDeclaration.Identifier.Text
            : $"{namespaceName}.{typeDeclaration.Identifier.Text}";

        symbols.Add(CreateSymbol(
            typeDeclaration.Identifier.Text,
            kind,
            fullyQualifiedName,
            namespaceName,
            typeDeclaration));

        ExtractBaseTypes(
            typeDeclaration,
            fullyQualifiedName,
            relationships);

        foreach (var property in typeDeclaration.Members
                     .OfType<PropertyDeclarationSyntax>())
        {
            symbols.Add(CreateSymbol(
                property.Identifier.Text,
                CodeSymbolKind.Property,
                $"{fullyQualifiedName}.{property.Identifier.Text}",
                fullyQualifiedName,
                property));
        }

        foreach (var field in typeDeclaration.Members
                     .OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                symbols.Add(CreateSymbol(
                    variable.Identifier.Text,
                    CodeSymbolKind.Field,
                    $"{fullyQualifiedName}.{variable.Identifier.Text}",
                    fullyQualifiedName,
                    field));
            }
        }

        foreach (var method in typeDeclaration.Members
                     .OfType<MethodDeclarationSyntax>())
        {
            symbols.Add(CreateSymbol(
                method.Identifier.Text,
                CodeSymbolKind.Method,
                $"{fullyQualifiedName}.{method.Identifier.Text}",
                fullyQualifiedName,
                method));
        }
    }

    private static void ExtractBaseTypes(
        TypeDeclarationSyntax typeDeclaration,
        string sourceType,
        List<CodeTypeRelationship> relationships)
    {
        if (typeDeclaration.BaseList is null)
        {
            return;
        }

        foreach (var baseType in typeDeclaration.BaseList.Types)
        {
            var targetType = baseType.Type.ToString();

            relationships.Add(
                new CodeTypeRelationship(
                    sourceType,
                    targetType,
                    "INHERITS_OR_IMPLEMENTS"));
        }
    }

    private static CodeSymbol CreateSymbol(
        string name,
        CodeSymbolKind kind,
        string fullyQualifiedName,
        string? parentFullyQualifiedName,
        SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();

        return new CodeSymbol(
            name,
            kind,
            fullyQualifiedName,
            parentFullyQualifiedName,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1);
    }
}
