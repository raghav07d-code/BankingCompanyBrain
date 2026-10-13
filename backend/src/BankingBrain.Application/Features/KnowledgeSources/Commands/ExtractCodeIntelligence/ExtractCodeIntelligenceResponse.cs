namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractCodeIntelligence;

public sealed record ExtractCodeIntelligenceResponse(
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
