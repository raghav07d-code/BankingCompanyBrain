using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSource;

public sealed record GetKnowledgeSourceQuery(
    Guid WorkspaceId,
    Guid KnowledgeSourceId)
    : IQuery<GetKnowledgeSourceResponse>;