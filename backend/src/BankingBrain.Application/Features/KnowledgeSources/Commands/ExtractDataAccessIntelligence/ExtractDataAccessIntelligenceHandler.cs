using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.DataAccessIntelligence;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractDataAccessIntelligence;

public sealed class ExtractDataAccessIntelligenceHandler
    : ICommandHandler<ExtractDataAccessIntelligenceCommand, ExtractDataAccessIntelligenceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;
    private readonly IDataAccessIntelligenceExtractionService _extractionService;

    public ExtractDataAccessIntelligenceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository,
        IDataAccessIntelligenceExtractionService extractionService)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
        _extractionService = extractionService;
    }

    public async Task<Result<ExtractDataAccessIntelligenceResponse>> Handle(
        ExtractDataAccessIntelligenceCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ExtractDataAccessIntelligenceResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<ExtractDataAccessIntelligenceResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<ExtractDataAccessIntelligenceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        var summary = await _extractionService.ExtractAsync(
            workspace.Id,
            source.Id,
            cancellationToken);

        return Result<ExtractDataAccessIntelligenceResponse>.Success(
            new ExtractDataAccessIntelligenceResponse(
                summary.WorkspaceId,
                summary.KnowledgeSourceId,
                summary.AccessesDiscovered,
                summary.AccessesResolved,
                summary.AccessesUnresolved,
                summary.EntitiesInIndex,
                summary.RelationshipsPersisted,
                summary.RunsSucceeded,
                summary.RunsFailed,
                summary.RunsSkippedIdempotent));
    }
}
