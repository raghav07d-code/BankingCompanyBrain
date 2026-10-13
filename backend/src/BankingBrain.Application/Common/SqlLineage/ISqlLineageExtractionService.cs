namespace BankingBrain.Application.Common.SqlLineage;

public interface ISqlLineageExtractionService
{
    Task<SqlLineageExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);
}

public sealed record SqlLineageExtractionSummary(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    int FilesAnalyzed,
    int FilesSkippedUnchanged,
    int EntitiesInIndex,
    int RelationshipsPersisted,
    int UnresolvedReferences,
    int RunsSucceeded,
    int RunsFailed,
    int RunsSkippedIdempotent);
