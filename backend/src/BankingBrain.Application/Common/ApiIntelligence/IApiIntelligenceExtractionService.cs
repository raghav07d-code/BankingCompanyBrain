namespace BankingBrain.Application.Common.ApiIntelligence;

public interface IApiIntelligenceExtractionService
{
    /// <summary>On-demand extraction: uses the knowledge source's latest reconciled snapshot revision.</summary>
    Task<ApiIntelligenceExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);

    /// <summary>Extraction pinned to an exact, explicit revision's immutable snapshot - the path queued refresh work must use.</summary>
    Task<ApiIntelligenceExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken);
}

public sealed record ApiIntelligenceExtractionSummary(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    int ProjectsAnalyzed,
    int EndpointsDiscovered,
    int EndpointsResolved,
    int EndpointsUnresolved,
    int EntitiesInIndex,
    int RelationshipsPersisted,
    int RunsSucceeded,
    int RunsFailed,
    int RunsSkippedIdempotent);
