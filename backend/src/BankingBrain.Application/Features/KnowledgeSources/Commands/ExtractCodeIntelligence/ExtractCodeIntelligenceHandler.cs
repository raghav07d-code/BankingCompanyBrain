using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractCodeIntelligence;

public sealed class ExtractCodeIntelligenceHandler
    : ICommandHandler<ExtractCodeIntelligenceCommand, ExtractCodeIntelligenceResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IKnowledgeSourceRepository _sourceRepository;
    private readonly ICodeRepositorySemanticExtractionService _extractionService;

    public ExtractCodeIntelligenceHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        IKnowledgeSourceRepository sourceRepository,
        ICodeRepositorySemanticExtractionService extractionService)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _sourceRepository = sourceRepository;
        _extractionService = extractionService;
    }

    public async Task<Result<ExtractCodeIntelligenceResponse>> Handle(
        ExtractCodeIntelligenceCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ExtractCodeIntelligenceResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);

        if (workspace is null)
            return Result<ExtractCodeIntelligenceResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _sourceRepository.GetByIdAsync(
            workspace.Id,
            request.KnowledgeSourceId,
            cancellationToken);

        if (source is null)
            return Result<ExtractCodeIntelligenceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    "Knowledge source was not found."));

        if (!string.Equals(source.ConnectorKey, "git", StringComparison.OrdinalIgnoreCase))
            return Result<ExtractCodeIntelligenceResponse>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_GIT_BACKED",
                    "Code intelligence extraction requires a Git-backed knowledge source."));

        var summary = await _extractionService.ExtractAsync(
            workspace.Id,
            source.Id,
            cancellationToken);

        return Result<ExtractCodeIntelligenceResponse>.Success(
            new ExtractCodeIntelligenceResponse(
                summary.WorkspaceId,
                summary.KnowledgeSourceId,
                summary.ProjectsAnalyzed,
                summary.SourceFilesAnalyzed,
                summary.FilesLinkedToDocuments,
                summary.EntitiesInIndex,
                summary.RelationshipsPersisted,
                summary.RunsSucceeded,
                summary.RunsFailed,
                summary.RunsSkippedIdempotent));
    }
}
