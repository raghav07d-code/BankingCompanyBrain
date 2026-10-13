using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Workspaces.Queries.GetWorkspace;

public sealed class GetWorkspaceHandler
    : IQueryHandler<GetWorkspaceQuery, GetWorkspaceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;

    public GetWorkspaceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<GetWorkspaceResponse>> Handle(
        GetWorkspaceQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result<GetWorkspaceResponse>.Failure(
                new Error(
                    "AUTH.UNAUTHORIZED",
                    "Authentication is required."));
        }

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
        {
            return Result<GetWorkspaceResponse>.Failure(
                new Error(
                    "WORKSPACE.NOT_FOUND",
                    "Workspace was not found."));
        }

        return Result<GetWorkspaceResponse>.Success(
            new GetWorkspaceResponse(
                workspace.Id,
                workspace.Name,
                workspace.Description,
                workspace.IsActive));
    }
}