using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.CreateKnowledgeSource;

public sealed record CreateKnowledgeSourceCommand(
    Guid WorkspaceId,
    string Name,
    string? Description,
    KnowledgeSourceType SourceType,
    string? ConnectorKey,
    string? ExternalIdentifier)
    : ICommand<CreateKnowledgeSourceResponse>;
