namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractApiIntelligence;

public sealed record ExtractApiIntelligenceResponse(
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
