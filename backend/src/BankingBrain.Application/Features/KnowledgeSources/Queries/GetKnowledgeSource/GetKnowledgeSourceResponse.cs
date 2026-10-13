using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSource;

public sealed record GetKnowledgeSourceResponse(
    Guid KnowledgeSourceId,
    Guid WorkspaceId,
    string Name,
    string? Description,
    KnowledgeSourceType SourceType,
    bool IsActive);