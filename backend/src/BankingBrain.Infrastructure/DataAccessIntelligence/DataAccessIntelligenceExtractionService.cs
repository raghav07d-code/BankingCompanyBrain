using System.Security.Cryptography;
using System.Text;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.DataAccessIntelligence;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Infrastructure.Connectors;

namespace BankingBrain.Infrastructure.DataAccessIntelligence;

/// <summary>
/// Orchestrates Data Access Intelligence for a Git-backed knowledge source: discovers
/// EF Core DbSet accesses with explicitly-declared table names (<see cref="EfDataAccessAnalyzer"/>),
/// then connects each one to an existing SQL Lineage SqlTable entity already persisted for
/// this workspace - never creating a new table entity on the C# side, since this extractor
/// has no authority to assert that a table exists; only SQL Lineage (reading real SQL
/// source) does. A table name with no matching SqlTable entity, or a DbSet whose entity type
/// has no explicit ToTable mapping at all, becomes an UNRESOLVED_REFERENCE rather than a
/// fabricated dependency. Persists through the same <c>PersistCodeObservationsAsync</c> path
/// as Code/API/SQL Intelligence - same idempotency, current-version filtering, and
/// workspace isolation, no new persistence code.
/// </summary>
public sealed class DataAccessIntelligenceExtractionService : IDataAccessIntelligenceExtractionService
{
    public const string ExtractorIdentity = "data-access-intelligence";
    public const string ExtractorVersion = "1.0";

    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ICodeProjectDiscovery _discovery;
    private readonly IEfDataAccessAnalyzer _analyzer;
    private readonly ISemanticExtractionRepository _semanticRepository;
    private readonly ConnectorPathPolicy _pathPolicy;

    public DataAccessIntelligenceExtractionService(
        IKnowledgeSourceRepository knowledgeSourceRepository,
        IDocumentRepository documentRepository,
        ICodeProjectDiscovery discovery,
        IEfDataAccessAnalyzer analyzer,
        ISemanticExtractionRepository semanticRepository,
        ConnectorPathPolicy pathPolicy)
    {
        _knowledgeSourceRepository = knowledgeSourceRepository;
        _documentRepository = documentRepository;
        _discovery = discovery;
        _analyzer = analyzer;
        _semanticRepository = semanticRepository;
        _pathPolicy = pathPolicy;
    }

    public async Task<DataAccessIntelligenceExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));

        if (knowledgeSourceId == Guid.Empty)
            throw new ArgumentException("Knowledge source ID is required.", nameof(knowledgeSourceId));

        var source = await _knowledgeSourceRepository.GetByIdAsync(workspaceId, knowledgeSourceId, cancellationToken);

        if (source is null)
            throw new KeyNotFoundException($"Knowledge source '{knowledgeSourceId}' was not found.");

        if (!string.Equals(source.ConnectorKey, "git", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Data access intelligence extraction requires a Git-backed knowledge source; '{source.Id}' uses connector '{source.ConnectorKey}'.");

        if (string.IsNullOrWhiteSpace(source.ExternalIdentifier))
            throw new InvalidOperationException($"Knowledge source '{source.Id}' does not have an external identifier.");

        if (string.IsNullOrWhiteSpace(source.LastSnapshotRevision))
            throw new InvalidOperationException(
                $"Knowledge source '{knowledgeSourceId}' has not been snapshotted yet - no immutable repository content is available to analyze.");

        // Bound to the immutable snapshot of the knowledge source's last reconciled
        // revision - never the connector's live, mutable working copy. Applied here for
        // the same reason as Code/API Intelligence: this analyzer also builds Roslyn
        // compilations from repository files, so it is equally exposed to a file changing
        // or disappearing out from under it if read from disk directly.
        var repositoryRoot = _pathPolicy.ResolveGitSnapshotRoot(knowledgeSourceId, source.LastSnapshotRevision);

        if (!Directory.Exists(repositoryRoot))
            throw new InvalidOperationException(
                $"Immutable snapshot for knowledge source '{knowledgeSourceId}' at revision '{source.LastSnapshotRevision}' was not found at '{repositoryRoot}'.");

        var documents = await _documentRepository.GetByKnowledgeSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);
        var documentIndex = BuildDocumentIndex(repositoryRoot, documents);

        var repositoryStructure = _discovery.Discover(repositoryRoot);
        var tableAccesses = _analyzer.Analyze(repositoryStructure);

        // Table names are matched against every SqlTable SQL Lineage has already persisted
        // for this workspace - case-insensitively, on the bare table name only (the C# side
        // carries no schema/database qualification), by the last dot-separated segment of
        // the SqlTable's own CanonicalName ("{db}.{schema}.{table}").
        var existingEntities = await _semanticRepository.GetEntitiesAsync(workspaceId, cancellationToken);
        var sqlTableExternalIdByTableName = existingEntities
            .Where(entity => entity.EntityType == "SqlTable")
            .GroupBy(entity => LastSegment(entity.CanonicalName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1) // an ambiguous (duplicate) bare name resolves to nothing, not a guess
            .ToDictionary(group => group.Key, group => group.First().ExternalIdentifier, StringComparer.OrdinalIgnoreCase);

        var entities = new List<CodeEntityObservation>();
        var relationshipsByVersion = new Dictionary<Guid, List<CodeRelationshipObservation>>();
        var discovered = 0;
        var resolved = 0;
        var unresolved = 0;

        foreach (var access in tableAccesses)
        {
            var documentVersionId = ResolveDocumentVersionId(documentIndex, access.AbsoluteFilePath);
            if (documentVersionId is null)
                continue;

            discovered++;

            var relativePath = RelativePath(documentIndex, access.AbsoluteFilePath);
            var sourceLocation = $"{relativePath}:{access.Line}-{access.Line}";
            var methodExternalId = $"csharp:{access.MethodFqn}";

            entities.Add(new CodeEntityObservation(null, "Method", access.MethodFqn, methodExternalId, null));

            if (!relationshipsByVersion.TryGetValue(documentVersionId.Value, out var relationshipList))
                relationshipsByVersion[documentVersionId.Value] = relationshipList = new List<CodeRelationshipObservation>();

            if (access.TableName is not null &&
                sqlTableExternalIdByTableName.TryGetValue(access.TableName, out var sqlTableExternalId) &&
                sqlTableExternalId is not null)
            {
                resolved++;
                relationshipList.Add(new CodeRelationshipObservation(
                    documentVersionId.Value,
                    sourceLocation,
                    methodExternalId,
                    sqlTableExternalId,
                    "METHOD_ACCESSES_TABLE",
                    1.0m));
            }
            else
            {
                unresolved++;
                var category = access.TableName is null
                    ? "no-explicit-table-mapping"
                    : "sql-table-not-ingested";
                var detail = access.TableName is null
                    ? $"entity type {access.EntityTypeFqn} has no explicit ToTable(...) configuration"
                    : $"no SqlTable named '{access.TableName}' exists in this workspace's SQL Lineage data";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{relativePath}|{access.Line}|{category}|{detail}")))[..32];
                var unresolvedId = $"data-access-unresolved:{hash}";

                entities.Add(new CodeEntityObservation(documentVersionId, "DataAccessUnresolvedReference", $"{category}: {detail}", unresolvedId, sourceLocation));
                relationshipList.Add(new CodeRelationshipObservation(
                    documentVersionId.Value,
                    sourceLocation,
                    methodExternalId,
                    unresolvedId,
                    "UNRESOLVED_REFERENCE",
                    0m));
            }
        }

        var fileRelationships = relationshipsByVersion
            .Select(entry => new CodeFileRelationshipBatch(entry.Key, entry.Value))
            .ToArray();

        var persisted = await _semanticRepository.PersistCodeObservationsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            entities,
            fileRelationships,
            cancellationToken);

        return new DataAccessIntelligenceExtractionSummary(
            workspaceId,
            knowledgeSourceId,
            discovered,
            resolved,
            unresolved,
            persisted.EntitiesInIndex,
            persisted.RelationshipsPersisted,
            persisted.RunsSucceeded,
            persisted.RunsFailed,
            persisted.RunsSkippedIdempotent);
    }

    private static string LastSegment(string canonicalName)
    {
        var lastDot = canonicalName.LastIndexOf('.');
        return lastDot < 0 ? canonicalName : canonicalName[(lastDot + 1)..];
    }

    private static CodeFileDocumentIndex BuildDocumentIndex(
        string repositoryRoot,
        IReadOnlyList<Document> documents)
    {
        var map = documents
            .Where(document =>
                document.IsActive &&
                document.CurrentVersionId is not null &&
                !string.IsNullOrWhiteSpace(document.ExternalIdentifier))
            .GroupBy(document => document.ExternalIdentifier!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().CurrentVersionId!.Value,
                StringComparer.Ordinal);

        return new CodeFileDocumentIndex(repositoryRoot, map);
    }

    private static Guid? ResolveDocumentVersionId(CodeFileDocumentIndex documentIndex, string absolutePath)
    {
        var relativePath = RelativePath(documentIndex, absolutePath);

        return documentIndex.DocumentVersionIdByRelativePath.TryGetValue(relativePath, out var versionId)
            ? versionId
            : null;
    }

    private static string RelativePath(CodeFileDocumentIndex documentIndex, string absolutePath) =>
        Path.GetRelativePath(documentIndex.RepositoryRootPath, absolutePath).Replace('\\', '/');
}
