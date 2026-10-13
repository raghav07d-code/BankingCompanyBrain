using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;

public sealed class GetDocumentVersionsHandler
    : IQueryHandler<GetDocumentVersionsQuery, IReadOnlyList<DocumentVersionResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentVersionRepository _repository;
    private readonly IWorkspaceRepository _workspaceRepository;

    public GetDocumentVersionsHandler(
        ICurrentUser currentUser,
        IDocumentVersionRepository repository,
        IWorkspaceRepository workspaceRepository)
    {
        _currentUser = currentUser;
        _repository = repository;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<IReadOnlyList<DocumentVersionResponse>>> Handle(
        GetDocumentVersionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<DocumentVersionResponse>>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        if (await _workspaceRepository.GetByIdAsync(
                _currentUser.UserId,
                request.WorkspaceId,
                cancellationToken) is null)
            return Result<IReadOnlyList<DocumentVersionResponse>>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var document = await _repository.GetDocumentAsync(
            request.WorkspaceId,
            request.DocumentId,
            cancellationToken);
        if (document is null)
            return Result<IReadOnlyList<DocumentVersionResponse>>.Failure(
                new Error("DOCUMENT.NOT_FOUND", "Document was not found."));

        var versions = await _repository.GetByDocumentAsync(
            request.WorkspaceId,
            document.Id,
            cancellationToken);
        return Result<IReadOnlyList<DocumentVersionResponse>>.Success(
            versions.Select(ToResponse).ToList());
    }

    internal static DocumentVersionResponse ToResponse(
        BankingBrain.Domain.Entities.DocumentVersion.DocumentVersion version)
    {
        return new DocumentVersionResponse(
            version.Id,
            version.DocumentId,
            version.VersionNumber,
            version.ContentHash,
            version.MediaType,
            version.FileName,
            version.FileSize,
            version.ProcessingState,
            version.CreatedOnUtc,
            version.ProcessingStartedOnUtc,
            version.ProcessingCompletedOnUtc,
            version.ProcessingError,
            version.ProcessorVersion);
    }
}