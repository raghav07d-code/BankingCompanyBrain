using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.Workspace;

namespace BankingBrain.Application.Features.Workspaces.Commands.CreateWorkspace;

public sealed class CreateWorkspaceHandler
    : ICommandHandler<CreateWorkspaceCommand, CreateWorkspaceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;

    public CreateWorkspaceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<CreateWorkspaceResponse>> Handle(
        CreateWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result<CreateWorkspaceResponse>.Failure(
                new Error(
                    "AUTH.UNAUTHORIZED",
                    "Authentication is required."));
        }

        var ownerUserId = _currentUser.UserId;

        var exists = await _workspaceRepository.ExistsAsync(
            ownerUserId,
            request.Name.Trim(),
            cancellationToken);

        if (exists)
        {
            return Result<CreateWorkspaceResponse>.Failure(
                new Error(
                    "WORKSPACE.DUPLICATE",
                    "A workspace with this name already exists."));
        }

        var workspace = Workspace.Create(
            ownerUserId,
            request.Name,
            request.Description);

        await _workspaceRepository.AddAsync(
            workspace,
            cancellationToken);

        var saved = await _workspaceRepository.SaveChangesAsync(
            cancellationToken);

        if (!saved)
        {
            return Result<CreateWorkspaceResponse>.Failure(
                new Error(
                    "WORKSPACE.DUPLICATE",
                    "A workspace with this name already exists."));
        }

        var response = new CreateWorkspaceResponse(
            workspace.Id,
            workspace.OwnerUserId,
            workspace.Name,
            workspace.Description);

        return Result<CreateWorkspaceResponse>.Success(response);
    }
}