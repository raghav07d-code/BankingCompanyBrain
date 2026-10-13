namespace BankingBrain.Application.Common.DataAccessIntelligence;

public interface IDataAccessIntelligenceExtractionService
{
    Task<DataAccessIntelligenceExtractionSummary> ExtractAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);
}

public sealed record DataAccessIntelligenceExtractionSummary(
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
