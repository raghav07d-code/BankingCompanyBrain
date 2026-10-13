using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Enums;
using BankingBrain.Domain.Entities.ProcessingJob;

namespace BankingBrain.Application.Features.Documents.Commands.IngestDocument;

public sealed class IngestDocumentHandler
    : ICommandHandler<IngestDocumentCommand, IngestDocumentResponse>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentVersionRepository _versionRepository;
    private readonly IObjectStorage _storage;
    private readonly IProcessingJobRepository _jobRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IDocumentUploadPolicy _uploadPolicy;

    public IngestDocumentHandler(
        ICurrentUser currentUser,
        IDocumentVersionRepository versionRepository,
        IObjectStorage storage,
        IProcessingJobRepository jobRepository,
        IWorkspaceRepository workspaceRepository,
        IDocumentUploadPolicy uploadPolicy)
    {
        _currentUser = currentUser;
        _versionRepository = versionRepository;
        _storage = storage;
        _jobRepository = jobRepository;
        _workspaceRepository = workspaceRepository;
        _uploadPolicy = uploadPolicy;
    }

    public async Task<Result<IngestDocumentResponse>> Handle(
        IngestDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IngestDocumentResponse>.Failure(
                new Error("AUTH.UNAUTHORIZED", "Authentication is required."));

        var workspace = await _workspaceRepository.GetByIdAsync(
            _currentUser.UserId,
            request.WorkspaceId,
            cancellationToken);
        if (workspace is null)
            return Result<IngestDocumentResponse>.Failure(
                new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));

        var document = await _versionRepository.GetDocumentAsync(
            workspace.Id,
            request.DocumentId,
            cancellationToken);
        if (document is null)
            return Result<IngestDocumentResponse>.Failure(
                new Error("DOCUMENT.NOT_FOUND", "Document was not found."));

        if (!_uploadPolicy.IsAllowed(request.FileName, request.MediaType))
            return Result<IngestDocumentResponse>.Failure(
                new Error("DOCUMENT.UNSUPPORTED_TYPE", "Document type is not supported."));

        var temporaryKey = $".pending/{Guid.NewGuid():N}";
        string? storageKeyForCompensation = null;
        string? contentHashForCompensation = null;
        var preserveFinalObject = false;
        try
        {
            var stored = await _storage.StoreAndHashAsync(
                request.WorkspaceId.ToString(), temporaryKey, request.Content,
                _uploadPolicy.MaxFileSize, cancellationToken);
            if (stored.FileSize == 0)
                return Result<IngestDocumentResponse>.Failure(
                    new Error("DOCUMENT.EMPTY", "The uploaded file is empty."));

            await using (var storedContent = await _storage.OpenReadAsync(
                request.WorkspaceId.ToString(), temporaryKey, cancellationToken))
            {
                if (!await _uploadPolicy.HasValidContentAsync(
                        request.FileName,
                        request.MediaType,
                        storedContent,
                        cancellationToken))
                    return Result<IngestDocumentResponse>.Failure(
                        new Error("DOCUMENT.INVALID_CONTENT", "The file content does not match a supported text format."));
            }

            var hash = stored.ContentHash;
            contentHashForCompensation = hash;
            var existing = await _versionRepository.GetByHashAsync(document.Id, hash, cancellationToken);
            if (existing is not null)
            {
                preserveFinalObject = true;
                return Result<IngestDocumentResponse>.Success(
                    new IngestDocumentResponse(existing.Id, document.Id, existing.VersionNumber,
                        existing.ContentHash, existing.ProcessingState, true));
            }

            var versionNumber = await _versionRepository.GetNextVersionNumberAsync(document.Id, cancellationToken);
            var storageKey = $"documents/{document.Id:N}/versions/{hash.ToLowerInvariant()}";
            storageKeyForCompensation = storageKey;
            await _storage.MoveAsync(request.WorkspaceId.ToString(), temporaryKey, storageKey, cancellationToken);

            var version = DocumentVersion.Create(document.Id, versionNumber, hash, request.MediaType,
                request.FileName, storageKey, stored.FileSize, storageKey, null);
            version.SetWorkspace(request.WorkspaceId);
            document.SetCurrentVersion(version.Id);
            await _versionRepository.AddAsync(version, cancellationToken);
            await _jobRepository.AddAsync(ProcessingJob.Create(version.Id), cancellationToken);

            if (!await _versionRepository.TrySaveChangesAsync(cancellationToken))
            {
                var concurrent = await _versionRepository.GetByHashAsync(document.Id, hash, cancellationToken);
                if (concurrent is not null)
                {
                    preserveFinalObject = true;
                    return Result<IngestDocumentResponse>.Success(
                        new IngestDocumentResponse(concurrent.Id, document.Id, concurrent.VersionNumber,
                            concurrent.ContentHash, concurrent.ProcessingState, true));
                }


                await _storage.DeleteAsync(
                    request.WorkspaceId.ToString(),
                    storageKey,
                    CancellationToken.None);
                preserveFinalObject = true;
                return Result<IngestDocumentResponse>.Failure(
                    new Error("DOCUMENT.CONFLICT", "Document ingestion conflicted with another request."));
            }

            preserveFinalObject = true;

            return Result<IngestDocumentResponse>.Success(
                new IngestDocumentResponse(version.Id, document.Id, version.VersionNumber,
                    version.ContentHash, version.ProcessingState, false));
        }
        catch
        {
            if (!preserveFinalObject &&
                storageKeyForCompensation is not null)
            {
                var persistedVersion = contentHashForCompensation is null
                    ? null
                    : await _versionRepository.GetByHashAsync(
                        document.Id,
                        contentHashForCompensation,
                        CancellationToken.None);
                if (persistedVersion is null)
                    await _storage.DeleteAsync(
                        request.WorkspaceId.ToString(),
                        storageKeyForCompensation,
                        CancellationToken.None);
            }

            throw;
        }
        finally
        {
            await _storage.DeleteAsync(request.WorkspaceId.ToString(), temporaryKey, CancellationToken.None);
        }
    }

}