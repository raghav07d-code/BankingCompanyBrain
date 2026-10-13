using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Workspaces.Queries.GetMyWorkspaces;

public sealed class GetMyWorkspacesHandler
    : IQueryHandler<
        GetMyWorkspacesQuery,
        IReadOnlyList<GetMyWorkspacesResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;

    public GetMyWorkspacesHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<IReadOnlyList<GetMyWorkspacesResponse>>> Handle(
        GetMyWorkspacesQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result<IReadOnlyList<GetMyWorkspacesResponse>>.Failure(
                new Error(
                    "AUTH.UNAUTHORIZED",
                    "Authentication is required."));
        }

        var workspaces = await _workspaceRepository.GetByOwnerAsync(
            _currentUser.UserId,
            cancellationToken);

        var response = workspaces
            .Select(workspace => new GetMyWorkspacesResponse(
                workspace.Id,
                workspace.Name,
                workspace.Description,
                workspace.IsActive))
            .ToList();

        return Result<IReadOnlyList<GetMyWorkspacesResponse>>.Success(response);
    }
}