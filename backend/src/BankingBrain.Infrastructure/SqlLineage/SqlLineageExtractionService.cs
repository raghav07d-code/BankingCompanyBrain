using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.SqlLineage;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Document;

namespace BankingBrain.Infrastructure.SqlLineage;

/// <summary>
/// Orchestrates SQL Lineage for a knowledge source, mirroring
/// <c>CodeRepositorySemanticExtractionService</c>'s shape and idempotency semantics exactly,
/// but reading each SQL file's content from the already-ingested DocumentSection text
/// (produced by <see cref="Processing.SqlDocumentProcessor"/>) instead of re-reading files
/// from the connector's on-disk working copy - SQL lineage is resolved per file from its own
/// statements, with no cross-file compilation step the way Roslyn needs, so there is no
/// reason to depend on the connector's filesystem path at all. Works for any connector
/// (git or filesystem) that produced `.sql`/`application/sql` Documents.
/// </summary>
public sealed class SqlLineageExtractionService : ISqlLineageExtractionService
{
    public const string ExtractorIdentity = "sql-lineage";
    public const string ExtractorVersion = "1.0";

    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ISemanticExtractionRepository _semanticRepository;

    public SqlLineageExtractionService(
        IKnowledgeSourceRepository knowledgeSourceRepository,
        IDocumentRepository documentRepository,
        ISemanticExtractionRepository semanticRepository)
    {
        _knowledgeSourceRepository = knowledgeSourceRepository;
        _documentRepository = documentRepository;
        _semanticRepository = semanticRepository;
    }

    public async Task<SqlLineageExtractionSummary> ExtractAsync(
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

        var documents = await _documentRepository.GetByKnowledgeSourceAsync(workspaceId, knowledgeSourceId, cancellationToken);

        var sqlDocuments = documents
            .Where(document =>
                document.IsActive &&
                document.CurrentVersionId is not null &&
                string.Equals(document.MimeType, "application/sql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidateVersionIds = sqlDocuments.Select(document => document.CurrentVersionId!.Value).Distinct().ToArray();

        var alreadyExtracted = await _semanticRepository.GetExtractedVersionIdsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            candidateVersionIds,
            cancellationToken);

        var defaultDatabase = Slug(source.Name);
        var analyzer = new SqlLineageScriptAnalyzer();

        var allEntities = new List<CodeEntityObservation>();
        var fileRelationships = new List<CodeFileRelationshipBatch>();
        var filesAnalyzed = 0;
        var unresolvedTotal = 0;

        foreach (var document in sqlDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versionId = document.CurrentVersionId!.Value;
            if (alreadyExtracted.Contains(versionId))
                continue;

            var relativePath = string.IsNullOrWhiteSpace(document.ExternalIdentifier)
                ? document.Name
                : document.ExternalIdentifier!;

            var source2 = await _semanticRepository.GetSourceByVersionAsync(workspaceId, versionId, cancellationToken);
            if (source2 is null)
                continue;

            var text = string.Join(
                "\n",
                source2.Sections.OrderBy(section => section.Sequence).Select(section => section.Text ?? string.Empty));

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var result = analyzer.Analyze(text, relativePath, defaultDatabase);
            filesAnalyzed++;
            unresolvedTotal += result.UnresolvedCount;

            allEntities.AddRange(result.Entities);

            if (result.Relationships.Count > 0)
            {
                var versioned = result.Relationships
                    .Select(relationship => relationship with { DocumentVersionId = versionId })
                    .ToArray();
                fileRelationships.Add(new CodeFileRelationshipBatch(versionId, versioned));
            }
        }

        var persisted = await _semanticRepository.PersistCodeObservationsAsync(
            workspaceId,
            ExtractorIdentity,
            ExtractorVersion,
            allEntities,
            fileRelationships,
            cancellationToken);

        return new SqlLineageExtractionSummary(
            workspaceId,
            knowledgeSourceId,
            filesAnalyzed,
            alreadyExtracted.Count,
            persisted.EntitiesInIndex,
            persisted.RelationshipsPersisted,
            unresolvedTotal,
            persisted.RunsSucceeded,
            persisted.RunsFailed,
            persisted.RunsSkippedIdempotent);
    }

    private static string Slug(string name)
    {
        var trimmed = name.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        return builder.ToString();
    }
}
