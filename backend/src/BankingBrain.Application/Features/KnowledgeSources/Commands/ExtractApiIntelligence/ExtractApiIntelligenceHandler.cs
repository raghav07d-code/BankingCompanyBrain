using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractApiIntelligence;

public sealed class ExtractApiIntelligenceHandler
    : ICommandHandler<ExtractApiIntelligenceCommand, ExtractApiIntelligenceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;
    private readonly IApiIntelligenceExtractionService _extractionService;

    public ExtractApiIntelligenceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository,
        IApiIntelligenceExtractionService extractionService)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
        _extractionService = extractionService;
    }

    public async Task<Result<ExtractApiIntelligenceResponse>> Handle(
        ExtractApiIntelligenceCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ExtractApiIntelligenceResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<ExtractApiIntelligenceResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<ExtractApiIntelligenceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        if (!string.Equals(source.ConnectorKey, "git", StringComparison.OrdinalIgnoreCase))
            return Result<ExtractApiIntelligenceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_GIT_BACKED",
                    "API intelligence extraction requires a Git-backed knowledge source."));

        var summary = await _extractionService.ExtractAsync(
            workspace.Id,
            source.Id,
            cancellationToken);

        return Result<ExtractApiIntelligenceResponse>.Success(
            new ExtractApiIntelligenceResponse(
                summary.WorkspaceId,
                summary.KnowledgeSourceId,
                summary.ProjectsAnalyzed,
                summary.EndpointsDiscovered,
                summary.EndpointsResolved,
                summary.EndpointsUnresolved,
                summary.EntitiesInIndex,
                summary.RelationshipsPersisted,
                summary.RunsSucceeded,
                summary.RunsFailed,
                summary.RunsSkippedIdempotent));
    }
}
