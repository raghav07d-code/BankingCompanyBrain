using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Workspaces.Queries.GetMyWorkspaces;

public sealed record GetMyWorkspacesQuery
    : IQuery<IReadOnlyList<GetMyWorkspacesResponse>>;