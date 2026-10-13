namespace BankingBrain.Application.Common.CodeIntelligence.Semantics;

public interface ICodeRepositorySemanticExtractionService
{
    /// <summary>On-demand extraction: uses the knowledge source's latest reconciled snapshot revision.</summary>
    Task<CodeRepositorySemanticExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);

    /// <summary>Extraction pinned to an exact, explicit revision's immutable snapshot - the path queued refresh work must use.</summary>
    Task<CodeRepositorySemanticExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken);
}

public sealed record CodeRepositorySemanticExtractionSummary(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    int ProjectsAnalyzed,
    int SourceFilesAnalyzed,
    int FilesLinkedToDocuments,
    int EntitiesInIndex,
    int RelationshipsPersisted,
    int RunsSucceeded,
    int RunsFailed,
    int RunsSkippedIdempotent);
