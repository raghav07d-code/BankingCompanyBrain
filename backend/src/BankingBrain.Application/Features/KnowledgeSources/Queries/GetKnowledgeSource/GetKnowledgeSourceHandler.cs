using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSource;

public sealed class GetKnowledgeSourceHandler
    : IQueryHandler<GetKnowledgeSourceQuery, GetKnowledgeSourceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;

    public GetKnowledgeSourceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<Result<GetKnowledgeSourceResponse>> Handle(
        GetKnowledgeSourceQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<GetKnowledgeSourceResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<GetKnowledgeSourceResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<GetKnowledgeSourceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        return Result<GetKnowledgeSourceResponse>.Success(
            new GetKnowledgeSourceResponse(
                source.Id,
                source.WorkspaceId,
                source.Name,
                source.Description,
                source.SourceType,
                source.IsActive));
    }
}