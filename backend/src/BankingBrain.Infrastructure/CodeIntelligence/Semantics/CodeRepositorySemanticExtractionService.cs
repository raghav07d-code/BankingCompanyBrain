using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Infrastructure.Connectors;

namespace BankingBrain.Infrastructure.CodeIntelligence.Semantics;

/// <summary>
/// Orchestrates Code Intelligence for a Git-backed knowledge source: discovers the
/// repository's projects from the same on-disk working copy the Git connector reads,
/// builds project-aware Roslyn compilations, maps the result onto the existing semantic
/// model, and persists it - without re-reading file content already captured as
/// Documents/DocumentVersions by GitSnapshotReconciliationService. Repository revision
/// is not tracked as a separate value: DocumentVersion identity is already content-hash
/// derived, so re-running against an unchanged revision naturally re-resolves to the same
/// DocumentVersionIds and the existing SemanticExtractionRun idempotency check
/// (workspace + version + extractor identity/version) short-circuits re-extraction.
/// </summary>
public sealed class CodeRepositorySemanticExtractionService : ICodeRepositorySemanticExtractionService
{
    public const string ExtractorIdentity = "roslyn-csharp";
    public const string ExtractorVersion = "1.0";

    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ICodeProjectDiscovery _discovery;
    private readonly ICodeRepositoryAnalyzer _analyzer;
    private readonly ICodeRepositorySemanticMapper _mapper;
    private readonly ISemanticExtractionRepository _semanticRepository;
    private readonly ConnectorPathPolicy _pathPolicy;

    public CodeRepositorySemanticExtractionService(
        IKnowledgeSourceRepository knowledgeSourceRepository,
        IDocumentRepository documentRepository,
        ICodeProjectDiscovery discovery,
        ICodeRepositoryAnalyzer analyzer,
        ICodeRepositorySemanticMapper mapper,
        ISemanticExtractionRepository semanticRepository,
        ConnectorPathPolicy pathPolicy)
    {
        _knowledgeSourceRepository = knowledgeSourceRepository;
        _documentRepository = documentRepository;
        _discovery = discovery;
        _analyzer = analyzer;
        _mapper = mapper;
        _semanticRepository = semanticRepository;
        _pathPolicy = pathPolicy;
    }

    public async Task<CodeRepositorySemanticExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        var source = await RequireGitSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);

        if (string.IsNullOrWhiteSpace(source.LastSnapshotRevision))
            throw new InvalidOperationException(
                $"Knowledge source '{knowledgeSourceId}' has not been snapshotted yet - no immutable repository content is available to analyze.");

        return await ExtractAsync(workspaceId, knowledgeSourceId, source.LastSnapshotRevision, cancellationToken);
    }

    public async Task<CodeRepositorySemanticExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        var source = await RequireGitSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);

        // Bound to the exact immutable snapshot this call was asked to analyze - never
        // the connector's live, mutable working copy, which may already reflect a later
        // commit by the time this actually runs (extraction is typically queued, not
        // synchronous with the snapshot request).
        var repositoryRoot = _pathPolicy.ResolveGitSnapshotRoot(knowledgeSourceId, revision);

        if (!Directory.Exists(repositoryRoot))
            throw new InvalidOperationException(
                $"Immutable snapshot for knowledge source '{knowledgeSourceId}' at revision '{revision}' was not found at '{repositoryRoot}'. It should have been written by snapshot reconciliation before extraction was queued.");

        var documents = await _documentRepository.GetByKnowledgeSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);
        var documentIndex = BuildDocumentIndex(repositoryRoot, documents);

        var unchangedVersionIds = await _semanticRepository.GetExtractedVersionIdsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            documentIndex.DocumentVersionIdByRelativePath.Values.Distinct().ToArray(),
            cancellationToken);

        var unchangedFilePaths = ResolveUnchangedFilePaths(documentIndex, unchangedVersionIds);

        var repositoryStructure = _discovery.Discover(repositoryRoot);
        var analysis = await _analyzer.AnalyzeAsync(repositoryStructure, unchangedFilePaths, cancellationToken);

        var observations = _mapper.Map(analysis, documentIndex, source.Name, source.ExternalIdentifier!);

        var fileRelationships = observations.Relationships
            .GroupBy(relationship => relationship.DocumentVersionId)
            .Select(group => new CodeFileRelationshipBatch(group.Key, group.ToArray()))
            .ToArray();

        var persisted = await _semanticRepository.PersistCodeObservationsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            observations.Entities,
            fileRelationships,
            cancellationToken);

        return new CodeRepositorySemanticExtractionSummary(
            workspaceId,
            knowledgeSourceId,
            repositoryStructure.Projects.Count,
            repositoryStructure.Projects.Sum(project => project.SourceFiles.Count),
            documentIndex.DocumentVersionIdByRelativePath.Count,
            persisted.EntitiesInIndex,
            persisted.RelationshipsPersisted,
            persisted.RunsSucceeded,
            persisted.RunsFailed,
            persisted.RunsSkippedIdempotent);
    }

    private async Task<Domain.Entities.KnowledgeSource.KnowledgeSource> RequireGitSourceAsync(
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
                $"Code intelligence extraction requires a Git-backed knowledge source; '{source.Id}' uses connector '{source.ConnectorKey}'.");

        if (string.IsNullOrWhiteSpace(source.ExternalIdentifier))
            throw new InvalidOperationException($"Knowledge source '{source.Id}' does not have an external identifier.");

        return source;
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

    /// <summary>
    /// Translates "already-extracted" DocumentVersionIds back into the absolute file paths
    /// the analyzer discovers on disk, so the Roslyn-analysis skip can be expressed purely
    /// in terms the (DB-unaware) ICodeRepositoryAnalyzer already understands.
    /// </summary>
    private static IReadOnlySet<string> ResolveUnchangedFilePaths(
        CodeFileDocumentIndex documentIndex,
        IReadOnlySet<Guid> unchangedVersionIds)
    {
        if (unchangedVersionIds.Count == 0)
            return new HashSet<string>();

        return documentIndex.DocumentVersionIdByRelativePath
            .Where(entry => unchangedVersionIds.Contains(entry.Value))
            .Select(entry => Path.GetFullPath(Path.Combine(documentIndex.RepositoryRootPath, entry.Key)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
