using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Documents.Commands.ReprocessDocumentVersion;

public sealed class ReprocessDocumentVersionHandler
    : ICommandHandler<ReprocessDocumentVersionCommand, ReprocessDocumentVersionResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentVersionRepository _repository;
    private readonly IProcessingJobRepository _jobRepository;
    private readonly IWorkspaceRepository _workspaceRepository;

    public ReprocessDocumentVersionHandler(
        ICurrentUser currentUser,
        IDocumentVersionRepository repository,
        IProcessingJobRepository jobRepository,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _repository = repository;
        _jobRepository = jobRepository;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<ReprocessDocumentVersionResponse>> Handle(
        ReprocessDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ReprocessDocumentVersionResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        if (await _workspaceRepository.GetByIdAsync(
                _currentUser.UserId,
                request.WorkspaceId,
                cancellationToken) is null)
            return Result<ReprocessDocumentVersionResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var version = await _repository.GetTrackedByIdAsync(
            request.WorkspaceId,
            request.DocumentId,
            request.VersionId,
            cancellationToken);
        if (version is null)
            return Result<ReprocessDocumentVersionResponse>.Failure(
                new Error("DOCUMENT_VERSION.NOT_FOUND", "Document version was not found."));

        try
        {
            version.ResetForReprocessing();
        }
        catch (InvalidOperationException exception)
        {
            return Result<ReprocessDocumentVersionResponse>.Failure(
                new Error("DOCUMENT_VERSION.INVALID_STATE", exception.Message));
        }

        if (!await _jobRepository.SupersedePendingAsync(version.Id, cancellationToken))
            return Result<ReprocessDocumentVersionResponse>.Failure(
                new Error("DOCUMENT_VERSION.BUSY", "Document version is already being processed."));

        await _repository.SaveChangesAsync(cancellationToken);
        await _jobRepository.TryAddAsync(
            BankingBrain.Domain.Entities.ProcessingJob.ProcessingJob.Create(version.Id),
            cancellationToken);
        return Result<ReprocessDocumentVersionResponse>.Success(
            new ReprocessDocumentVersionResponse(version.Id, version.ProcessingState));
    }
}