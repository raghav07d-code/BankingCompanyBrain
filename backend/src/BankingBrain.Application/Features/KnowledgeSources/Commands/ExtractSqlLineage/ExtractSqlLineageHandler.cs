using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Common.SqlLineage;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractSqlLineage;

public sealed class ExtractSqlLineageHandler
    : ICommandHandler<ExtractSqlLineageCommand, ExtractSqlLineageResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;
    private readonly ISqlLineageExtractionService _extractionService;

    public ExtractSqlLineageHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository,
        ISqlLineageExtractionService extractionService)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
        _extractionService = extractionService;
    }

    public async Task<Result<ExtractSqlLineageResponse>> Handle(
        ExtractSqlLineageCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ExtractSqlLineageResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<ExtractSqlLineageResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<ExtractSqlLineageResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        var summary = await _extractionService.ExtractAsync(
            workspace.Id,
            source.Id,
            cancellationToken);

        return Result<ExtractSqlLineageResponse>.Success(
            new ExtractSqlLineageResponse(
                summary.WorkspaceId,
                summary.KnowledgeSourceId,
                summary.FilesAnalyzed,
                summary.FilesSkippedUnchanged,
                summary.EntitiesInIndex,
                summary.RelationshipsPersisted,
                summary.UnresolvedReferences,
                summary.RunsSucceeded,
                summary.RunsFailed,
                summary.RunsSkippedIdempotent));
    }
}
