namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractSqlLineage;

public sealed record ExtractSqlLineageResponse(
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
