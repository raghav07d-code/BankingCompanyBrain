using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Infrastructure.Connectors;

namespace BankingBrain.Infrastructure.ApiIntelligence;

/// <summary>
/// Orchestrates API Intelligence for a Git-backed knowledge source, mirroring
/// <c>CodeRepositorySemanticExtractionService</c> exactly: discovers the repository's
/// projects from the same on-disk working copy the Git connector reads, runs
/// <see cref="ApiEndpointDiscoveryAnalyzer"/> against project-aware Roslyn compilations,
/// and persists the result onto the existing semantic model through the same
/// <c>PersistCodeObservationsAsync</c> path Code Intelligence and SQL Lineage both use -
/// same idempotency semantics (workspace + version + extractor identity/version), same
/// current-version filtering, same workspace isolation, no new persistence code.
/// </summary>
public sealed class ApiIntelligenceExtractionService : IApiIntelligenceExtractionService
{
    public const string ExtractorIdentity = "api-intelligence";
    public const string ExtractorVersion = "1.0";

    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ICodeProjectDiscovery _discovery;
    private readonly IApiEndpointDiscoveryAnalyzer _analyzer;
    private readonly ISemanticExtractionRepository _semanticRepository;
    private readonly ConnectorPathPolicy _pathPolicy;

    public ApiIntelligenceExtractionService(
        IKnowledgeSourceRepository knowledgeSourceRepository,
        IDocumentRepository documentRepository,
        ICodeProjectDiscovery discovery,
        IApiEndpointDiscoveryAnalyzer analyzer,
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

    public async Task<ApiIntelligenceExtractionSummary> ExtractAsync(
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

    public async Task<ApiIntelligenceExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        var source = await RequireGitSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);

        var repositoryRoot = _pathPolicy.ResolveGitSnapshotRoot(knowledgeSourceId, revision);

        if (!Directory.Exists(repositoryRoot))
            throw new InvalidOperationException(
                $"Immutable snapshot for knowledge source '{knowledgeSourceId}' at revision '{revision}' was not found at '{repositoryRoot}'. It should have been written by snapshot reconciliation before extraction was queued.");

        var documents = await _documentRepository.GetByKnowledgeSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);
        var documentIndex = BuildDocumentIndex(repositoryRoot, documents);

        var repositoryStructure = _discovery.Discover(repositoryRoot);

        var discovery = _analyzer.Analyze(repositoryStructure, documentIndex, source.ExternalIdentifier!);

        var fileRelationships = discovery.Relationships
            .GroupBy(relationship => relationship.DocumentVersionId)
            .Select(group => new CodeFileRelationshipBatch(group.Key, group.ToArray()))
            .ToArray();

        var persisted = await _semanticRepository.PersistCodeObservationsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            discovery.Entities,
            fileRelationships,
            cancellationToken);

        return new ApiIntelligenceExtractionSummary(
            workspaceId,
            knowledgeSourceId,
            repositoryStructure.Projects.Count,
            discovery.EndpointsDiscovered,
            discovery.EndpointsResolved,
            discovery.EndpointsUnresolved,
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
                $"API intelligence extraction requires a Git-backed knowledge source; '{source.Id}' uses connector '{source.ConnectorKey}'.");

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
}
