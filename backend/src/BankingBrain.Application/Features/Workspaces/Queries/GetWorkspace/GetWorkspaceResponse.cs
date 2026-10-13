namespace BankingBrain.Application.Features.Workspaces.Queries.GetWorkspace;

public sealed record GetWorkspaceResponse(
    Guid WorkspaceId,
    string Name,
    string? Description,
    bool IsActive);