using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Semantics.Commands.ExtractSemantics;

public sealed class ExtractSemanticsHandler : ICommandHandler<ExtractSemanticsCommand, ExtractSemanticsResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ISemanticExtractionRepository _repository;
    private readonly ISemanticExtractor _extractor;

    public ExtractSemanticsHandler(
        ICurrentUser currentUser,
        IWorkspaceRepository workspaceRepository,
        ISemanticExtractionRepository repository,
        ISemanticExtractor extractor)
    {
        _currentUser = currentUser;
        _workspaceRepository = workspaceRepository;
        _repository = repository;
        _extractor = extractor;
    }

    public async Task<Result<ExtractSemanticsResponse>> Handle(ExtractSemanticsCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ExtractSemanticsResponse>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaceRepository.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null)
            return Result<ExtractSemanticsResponse>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var source = await _repository.GetSourceAsync(request.WorkspaceId, request.DocumentId, request.VersionId, cancellationToken);
        if (source is null)
            return Result<ExtractSemanticsResponse>.Failure(new Error("DOCUMENT_VERSION.NOT_FOUND", "Document version was not found."));
        if (source.ProcessingState != DocumentProcessingState.Processed)
            return Result<ExtractSemanticsResponse>.Failure(new Error("SEMANTIC_EXTRACTION.NOT_READY", "Document version must be processed before semantic extraction."));

        var successful = await _repository.GetSuccessfulAsync(request.WorkspaceId, request.VersionId, _extractor.Identity, _extractor.Version, cancellationToken);
        if (successful is not null)
            return ToResponse(successful);

        var active = await _repository.GetActiveAsync(request.WorkspaceId, request.VersionId, _extractor.Identity, _extractor.Version, cancellationToken);
        if (active is not null)
            return ToResponse(active);

        var run = Domain.Entities.Semantics.SemanticExtractionRun.Start(request.WorkspaceId, request.VersionId, _extractor.Identity, _extractor.Version);
        var job = Domain.Entities.Semantics.SemanticProcessingJob.Create(request.WorkspaceId, request.VersionId, run.Id, _extractor.Identity, _extractor.Version);
        if (!await _repository.TryQueueAsync(run, job, cancellationToken))
        {
            var queued = await _repository.GetActiveAsync(request.WorkspaceId, request.VersionId, _extractor.Identity, _extractor.Version, cancellationToken);
            if (queued is not null) return ToResponse(queued);
            return Result<ExtractSemanticsResponse>.Failure(new Error("SEMANTIC_EXTRACTION.CONFLICT", "Semantic extraction is already being scheduled."));
        }

        return ToResponse(run, job.Id, job.Status);
    }

    private static Result<ExtractSemanticsResponse> ToResponse(Domain.Entities.Semantics.SemanticExtractionRun run) =>
        Result<ExtractSemanticsResponse>.Success(new ExtractSemanticsResponse(
            run.Id, run.DocumentVersionId, run.ExtractorIdentity, run.ExtractorVersion,
            run.Status, run.EntityCount, run.ClaimCount, run.RelationshipCount));

    private static Result<ExtractSemanticsResponse> ToResponse(Domain.Entities.Semantics.SemanticExtractionRun run, Guid jobId, ProcessingJobStatus jobStatus) =>
        Result<ExtractSemanticsResponse>.Success(new ExtractSemanticsResponse(
            run.Id, run.DocumentVersionId, run.ExtractorIdentity, run.ExtractorVersion,
            run.Status, run.EntityCount, run.ClaimCount, run.RelationshipCount, jobId, jobStatus));
}
