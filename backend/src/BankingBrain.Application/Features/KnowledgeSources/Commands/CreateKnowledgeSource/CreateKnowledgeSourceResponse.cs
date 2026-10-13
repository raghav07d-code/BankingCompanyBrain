using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.CreateKnowledgeSource;

public sealed record CreateKnowledgeSourceResponse(
    Guid KnowledgeSourceId,
    Guid WorkspaceId,
    string Name,
    string? Description,
    KnowledgeSourceType SourceType,
    bool IsActive);