using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSources;

public sealed class GetKnowledgeSourcesHandler
    : IQueryHandler<
        GetKnowledgeSourcesQuery,
        IReadOnlyList<GetKnowledgeSourcesResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;

    public GetKnowledgeSourcesHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<Result<IReadOnlyList<GetKnowledgeSourcesResponse>>> Handle(
        GetKnowledgeSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var workspaceResult = await GetWorkspaceAsync(
            request.WorkspaceId,
            cancellationToken);

        if (workspaceResult is not null)
            return Result<IReadOnlyList<GetKnowledgeSourcesResponse>>.Failure(
                workspaceResult);

        var sources = await _sourceRepository.GetByWorkspaceAsync(
            request.WorkspaceId,
            cancellationToken);

        return Result<IReadOnlyList<GetKnowledgeSourcesResponse>>.Success(
            sources.Select(source => new GetKnowledgeSourcesResponse(
                source.Id,
                source.WorkspaceId,
                source.Name,
                source.Description,
                source.SourceType,
                source.IsActive)).ToList());
    }

    private async Task<Error?> GetWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return new Error("AUTH.UNAUTHORIZED", "Authentication is required.");

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            workspaceId,
            cancellationToken);

        return workspace is null
            ? new Error("WORKSPACE.NOT_FOUND", "Workspace was not found.")
            : null;
    }
}