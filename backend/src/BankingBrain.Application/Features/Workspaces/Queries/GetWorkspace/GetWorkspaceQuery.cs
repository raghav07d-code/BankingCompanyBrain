using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Workspaces.Queries.GetWorkspace;

public sealed record GetWorkspaceQuery(Guid WorkspaceId)
    : IQuery<GetWorkspaceResponse>;