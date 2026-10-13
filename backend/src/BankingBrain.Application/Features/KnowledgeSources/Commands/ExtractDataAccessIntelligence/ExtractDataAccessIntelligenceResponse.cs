namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractDataAccessIntelligence;

public sealed record ExtractDataAccessIntelligenceResponse(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    int AccessesDiscovered,
    int AccessesResolved,
    int AccessesUnresolved,
    int EntitiesInIndex,
    int RelationshipsPersisted,
    int RunsSucceeded,
    int RunsFailed,
    int RunsSkippedIdempotent);
