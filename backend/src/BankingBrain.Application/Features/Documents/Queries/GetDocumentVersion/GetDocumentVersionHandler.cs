using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersion;

public sealed class GetDocumentVersionHandler
    : IQueryHandler<GetDocumentVersionQuery, GetDocumentVersionResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentVersionRepository _repository;
    private readonly IWorkspaceRepository _workspaceRepository;

    public GetDocumentVersionHandler(
        ICurrentUser currentUser,
        IDocumentVersionRepository repository,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _repository = repository;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<GetDocumentVersionResponse>> Handle(
        GetDocumentVersionQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<GetDocumentVersionResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        if (await _workspaceRepository.GetByIdAsync(
                _currentUser.UserId,
                request.WorkspaceId,
                cancellationToken) is null)
            return Result<GetDocumentVersionResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var version = await _repository.GetByIdAsync(
            request.WorkspaceId,
            request.DocumentId,
            request.VersionId,
            cancellationToken);
        if (version is null)
            return Result<GetDocumentVersionResponse>.Failure(
                new Error("DOCUMENT_VERSION.NOT_FOUND", "Document version was not found."));

        var attempts = await _repository.GetAttemptsAsync(
            version.Id,
            cancellationToken);
        return Result<GetDocumentVersionResponse>.Success(
            new GetDocumentVersionResponse(
                GetDocumentVersionsHandler.ToResponse(version),
                attempts.Select(attempt => new ProcessingAttemptResponse(
                    attempt.Id,
                    attempt.AttemptNumber,
                    attempt.StartedOnUtc,
                    attempt.CompletedOnUtc,
                    attempt.ProcessorIdentity,
                    attempt.ProcessorVersion,
                    attempt.Succeeded,
                    attempt.Error)).ToList()));
    }
}