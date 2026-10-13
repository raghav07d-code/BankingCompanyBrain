using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSources;

public sealed record GetKnowledgeSourcesQuery(Guid WorkspaceId)
    : IQuery<IReadOnlyList<GetKnowledgeSourcesResponse>>;