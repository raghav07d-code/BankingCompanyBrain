using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSources;

public sealed record GetKnowledgeSourcesResponse(
    Guid KnowledgeSourceId,
    Guid WorkspaceId,
    string Name,
    string? Description,
    KnowledgeSourceType SourceType,
    bool IsActive);