using BankingBrain.Application.Common.CodeIntelligence.Projects;

namespace BankingBrain.Application.Common.CodeIntelligence.Semantics;

public sealed class CodeRepositorySemanticMapper : ICodeRepositorySemanticMapper
{
    private const decimal StructuralConfidence = 1.0m;

    public CodeRepositorySemanticObservations Map(
        CodeRepositoryAnalysisResult analysis,
        CodeFileDocumentIndex documentIndex,
        string repositoryCanonicalName,
        string repositoryExternalIdentifier)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(documentIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryCanonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryExternalIdentifier);

        var entities = new List<CodeEntityObservation>();
        var relationships = new List<CodeRelationshipObservation>();

        var repositoryExternalId = $"csharp-repository:{repositoryExternalIdentifier}";

        entities.Add(
            new CodeEntityObservation(
                DocumentVersionId: null,
                EntityType: "Repository",
                CanonicalName: repositoryCanonicalName,
                ExternalIdentifier: repositoryExternalId,
                SourceLocation: null));

        var projectExternalIdByName = analysis.Projects.ToDictionary(
            project => project.Name,
            project => ProjectExternalId(RelativePath(documentIndex, project.ProjectFilePath)),
            StringComparer.Ordinal);

        foreach (var project in analysis.Projects)
        {
            var projectRelativePath = RelativePath(documentIndex, project.ProjectFilePath);
            var projectExternalId = ProjectExternalId(projectRelativePath);
            var projectDocumentVersionId = ResolveDocumentVersionId(documentIndex, project.ProjectFilePath);

            entities.Add(
                new CodeEntityObservation(
                    DocumentVersionId: projectDocumentVersionId,
                    EntityType: "Project",
                    CanonicalName: project.Name,
                    ExternalIdentifier: projectExternalId,
                    SourceLocation: projectRelativePath));

            if (projectDocumentVersionId is not null)
            {
                relationships.Add(
                    new CodeRelationshipObservation(
                        projectDocumentVersionId.Value,
                        projectRelativePath,
                        repositoryExternalId,
                        projectExternalId,
                        "REPOSITORY_CONTAINS_PROJECT",
                        StructuralConfidence));
            }

            var symbolKindByExternalId = new Dictionary<string, CodeSymbolKind>(StringComparer.Ordinal);

            foreach (var file in project.Files)
            {
                foreach (var symbol in file.Symbols)
                {
                    symbolKindByExternalId[SymbolExternalId(symbol.FullyQualifiedName)] = symbol.Kind;
                }
            }

            foreach (var file in project.Files)
            {
                var fileDocumentVersionId = ResolveDocumentVersionId(documentIndex, file.FilePath);
                var fileRelativePath = RelativePath(documentIndex, file.FilePath);

                foreach (var symbol in file.Symbols)
                {
                    entities.Add(
                        new CodeEntityObservation(
                            DocumentVersionId: fileDocumentVersionId,
                            EntityType: MapEntityType(symbol.Kind),
                            CanonicalName: symbol.FullyQualifiedName,
                            ExternalIdentifier: SymbolExternalId(symbol.FullyQualifiedName),
                            SourceLocation: $"{fileRelativePath}:{symbol.StartLine}-{symbol.EndLine}"));
                }

                if (fileDocumentVersionId is null)
                {
                    continue;
                }

                foreach (var symbol in file.Symbols)
                {
                    var location = $"{fileRelativePath}:{symbol.StartLine}-{symbol.EndLine}";

                    if (symbol.Kind == CodeSymbolKind.Namespace)
                    {
                        relationships.Add(
                            new CodeRelationshipObservation(
                                fileDocumentVersionId.Value,
                                location,
                                projectExternalId,
                                SymbolExternalId(symbol.FullyQualifiedName),
                                "PROJECT_CONTAINS_NAMESPACE",
                                StructuralConfidence));

                        continue;
                    }

                    if (symbol.ParentFullyQualifiedName is null)
                    {
                        continue;
                    }

                    var parentExternalId = SymbolExternalId(symbol.ParentFullyQualifiedName);

                    var containmentPredicate = MapContainmentPredicate(
                        symbol.Kind,
                        parentExternalId,
                        symbolKindByExternalId);

                    if (containmentPredicate is null)
                    {
                        continue;
                    }

                    relationships.Add(
                        new CodeRelationshipObservation(
                            fileDocumentVersionId.Value,
                            location,
                            parentExternalId,
                            SymbolExternalId(symbol.FullyQualifiedName),
                            containmentPredicate,
                            StructuralConfidence));
                }
            }

            foreach (var relationship in project.Relationships)
            {
                var predicate = MapPredicate(relationship.RelationshipKind);

                if (predicate is null)
                {
                    continue;
                }

                var documentVersionId = ResolveDocumentVersionId(documentIndex, relationship.FilePath);

                if (documentVersionId is null)
                {
                    continue;
                }

                relationships.Add(
                    new CodeRelationshipObservation(
                        documentVersionId.Value,
                        $"{RelativePath(documentIndex, relationship.FilePath)}:{relationship.StartLine}-{relationship.EndLine}",
                        SymbolExternalId(relationship.SourceSymbol),
                        SymbolExternalId(relationship.TargetSymbol),
                        predicate,
                        StructuralConfidence));
            }
        }

        foreach (var reference in analysis.ProjectReferences)
        {
            if (!projectExternalIdByName.TryGetValue(reference.SourceSymbol, out var subjectExternalId) ||
                !projectExternalIdByName.TryGetValue(reference.TargetSymbol, out var objectExternalId))
            {
                continue;
            }

            var documentVersionId = ResolveDocumentVersionId(documentIndex, reference.FilePath);

            if (documentVersionId is null)
            {
                continue;
            }

            relationships.Add(
                new CodeRelationshipObservation(
                    documentVersionId.Value,
                    $"{RelativePath(documentIndex, reference.FilePath)}:{reference.StartLine}-{reference.EndLine}",
                    subjectExternalId,
                    objectExternalId,
                    "PROJECT_REFERENCES_PROJECT",
                    StructuralConfidence));
        }

        return new CodeRepositorySemanticObservations(entities, relationships);
    }

    private static string? MapPredicate(string relationshipKind) => relationshipKind switch
    {
        "INHERITS" or "IMPLEMENTS" => "INHERITS_OR_IMPLEMENTS",
        "CALLS" => "CALLS",
        "METHOD_DEPENDS_ON_TYPE" => "METHOD_DEPENDS_ON_TYPE",
        "TYPE_DEPENDS_ON_TYPE" => "TYPE_DEPENDS_ON_TYPE",
        _ => null
    };

    /// <summary>
    /// Method/property/field containment is unambiguous (their parent is always a type).
    /// A type's parent is ambiguous - it may be a namespace (top-level type, emits
    /// NAMESPACE_CONTAINS_TYPE) or another type (nested type); nested-type containment has
    /// no requested predicate, so it is intentionally not emitted.
    /// </summary>
    private static string? MapContainmentPredicate(
        CodeSymbolKind kind,
        string parentExternalId,
        IReadOnlyDictionary<string, CodeSymbolKind> symbolKindByExternalId) => kind switch
    {
        CodeSymbolKind.Class or CodeSymbolKind.Interface or CodeSymbolKind.Struct
            or CodeSymbolKind.Record or CodeSymbolKind.Enum =>
            symbolKindByExternalId.TryGetValue(parentExternalId, out var parentKind) &&
            parentKind == CodeSymbolKind.Namespace
                ? "NAMESPACE_CONTAINS_TYPE"
                : null,
        CodeSymbolKind.Method => "TYPE_CONTAINS_METHOD",
        CodeSymbolKind.Property => "TYPE_CONTAINS_PROPERTY",
        CodeSymbolKind.Field => "TYPE_CONTAINS_FIELD",
        _ => null
    };

    private static string MapEntityType(CodeSymbolKind kind) => kind switch
    {
        CodeSymbolKind.Namespace => "Namespace",
        CodeSymbolKind.Class => "Class",
        CodeSymbolKind.Interface => "Interface",
        CodeSymbolKind.Struct => "Struct",
        CodeSymbolKind.Record => "Record",
        CodeSymbolKind.Enum => "Enum",
        CodeSymbolKind.Method => "Method",
        CodeSymbolKind.Property => "Property",
        CodeSymbolKind.Field => "Field",
        _ => "Class"
    };

    private static string SymbolExternalId(string fullyQualifiedName) => $"csharp:{fullyQualifiedName}";

    private static string ProjectExternalId(string relativeProjectPath) => $"csharp-project:{relativeProjectPath}";

    private static Guid? ResolveDocumentVersionId(CodeFileDocumentIndex documentIndex, string absolutePath)
    {
        var relativePath = RelativePath(documentIndex, absolutePath);

        return documentIndex.DocumentVersionIdByRelativePath.TryGetValue(relativePath, out var versionId)
            ? versionId
            : null;
    }

    private static string RelativePath(CodeFileDocumentIndex documentIndex, string absolutePath)
    {
        return Path.GetRelativePath(documentIndex.RepositoryRootPath, absolutePath)
            .Replace('\\', '/');
    }
}
