using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractSqlLineage;

public sealed record ExtractSqlLineageCommand(
    Guid WorkspaceId,
    Guid KnowledgeSourceId)
    : ICommand<ExtractSqlLineageResponse>;
