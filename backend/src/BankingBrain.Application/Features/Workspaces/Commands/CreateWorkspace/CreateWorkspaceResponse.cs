namespace BankingBrain.Application.Features.Workspaces.Commands.CreateWorkspace;

public sealed record CreateWorkspaceResponse(
    Guid WorkspaceId,
    Guid OwnerUserId,
    string Name,
    string? Description);