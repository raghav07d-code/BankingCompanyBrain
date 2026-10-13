using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Workspaces.Commands.CreateWorkspace;

public sealed record CreateWorkspaceCommand(
    string Name,
    string? Description) : ICommand<CreateWorkspaceResponse>;