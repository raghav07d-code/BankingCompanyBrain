namespace BankingBrain.Application.Features.Workspaces.Queries.GetMyWorkspaces;

public sealed record GetMyWorkspacesResponse(
    Guid WorkspaceId,
    string Name,
    string? Description,
    bool IsActive);