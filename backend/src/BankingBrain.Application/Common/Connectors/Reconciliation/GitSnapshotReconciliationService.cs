using System.Security.Cryptography;
using System.Text;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Domain.Entities.Document;
using BankingBrain.Domain.Entities.DocumentVersion;
using BankingBrain.Domain.Entities.ProcessingJob;
using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Common.Connectors.Reconciliation;

public sealed class GitSnapshotReconciliationService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentVersionRepository _versionRepository;
    private readonly IProcessingJobRepository _jobRepository;
    private readonly IObjectStorage _storage;

    public GitSnapshotReconciliationService(
        IDocumentRepository documentRepository,
        IDocumentVersionRepository versionRepository,
        IProcessingJobRepository jobRepository,
        IObjectStorage storage)
    {
        _documentRepository = documentRepository;
        _versionRepository = versionRepository;
        _jobRepository = jobRepository;
        _storage = storage;
    }

    public async Task<GitReconciliationResult> ReconcileAsync(
        SourceSnapshot snapshot,
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));

        if (knowledgeSourceId == Guid.Empty)
            throw new ArgumentException("Knowledge source ID is required.", nameof(knowledgeSourceId));

        if (!string.Equals(
                snapshot.ConnectorKey,
                "git",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Git reconciliation requires a Git source snapshot.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Revision))
            throw new InvalidOperationException(
                "Git snapshot revision is required.");

        var existingDocuments =
            await _documentRepository.GetByKnowledgeSourceAsync(
                workspaceId,
                knowledgeSourceId,
                cancellationToken);

        var seenExternalIdentifiers = new HashSet<string>(
            StringComparer.Ordinal);

        var createdDocuments = 0;
        var createdVersions = 0;
        var unchangedArtifacts = 0;
        var reactivatedDocuments = 0;
        var skippedArtifacts = 0;

        foreach (var artifact in snapshot.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (artifact.Kind != SourceArtifactKind.SourceCode &&
                artifact.Kind != SourceArtifactKind.File &&
                artifact.Kind != SourceArtifactKind.Configuration)
            {
                skippedArtifacts++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(artifact.ExternalIdentifier))
            {
                skippedArtifacts++;
                continue;
            }

            seenExternalIdentifiers.Add(artifact.ExternalIdentifier);

            if (artifact.Content is null)
            {
                skippedArtifacts++;
                continue;
            }

            var contentBytes = Encoding.UTF8.GetBytes(artifact.Content);

            if (contentBytes.Length == 0)
            {
                skippedArtifacts++;
                continue;
            }

            var contentHash = Convert.ToHexString(
                SHA256.HashData(contentBytes));

            var document =
                existingDocuments.FirstOrDefault(x =>
                    string.Equals(
                        x.ExternalIdentifier,
                        artifact.ExternalIdentifier,
                        StringComparison.Ordinal));

            if (document is null)
            {
                document = Document.Create(
                    workspaceId,
                    knowledgeSourceId,
                    artifact.Name,
                    artifact.ExternalIdentifier,
                    artifact.MediaType ?? "text/plain");

                await _documentRepository.AddAsync(
                    document,
                    cancellationToken);

                // Persist the Document before creating its first DocumentVersion.
                // DocumentVersion.DocumentId points to Document, while
                // Document.CurrentVersionId points back to DocumentVersion.
                // Saving the Document first prevents an EF circular dependency.
                if (!await _documentRepository.SaveChangesAsync(
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Git artifact '{artifact.ExternalIdentifier}' could not create its document.");
                }

                existingDocuments = existingDocuments
                    .Append(document)
                    .ToList();

                createdDocuments++;
            }
            else if (!document.IsActive)
            {
                document.Reactivate();
                reactivatedDocuments++;
            }

            var existingVersion =
                await _versionRepository.GetByHashAsync(
                    document.Id,
                    contentHash,
                    cancellationToken);

            if (existingVersion is not null)
            {
                if (document.CurrentVersionId != existingVersion.Id)
                    document.SetCurrentVersion(existingVersion.Id);

                await _versionRepository.SaveChangesAsync(
                    cancellationToken);

                unchangedArtifacts++;
                continue;
            }

            var versionNumber =
                await _versionRepository.GetNextVersionNumberAsync(
                    document.Id,
                    cancellationToken);

            var storageKey =
                $"documents/{document.Id:N}/versions/{contentHash.ToLowerInvariant()}";

            await using var contentStream =
                new MemoryStream(contentBytes, writable: false);

            await _storage.StoreAsync(
                workspaceId.ToString(),
                storageKey,
                contentStream,
                cancellationToken);

            var version = DocumentVersion.Create(
                document.Id,
                versionNumber,
                contentHash,
                artifact.MediaType ?? "text/plain",
                artifact.Name,
                storageKey,
                contentBytes.LongLength,
                storageKey,
                null);

            version.SetWorkspace(workspaceId);

            await _versionRepository.AddAsync(
                version,
                cancellationToken);

            await _jobRepository.AddAsync(
                ProcessingJob.Create(version.Id),
                cancellationToken);

            if (!await _versionRepository.TrySaveChangesAsync(
                    cancellationToken))
            {
                var concurrent =
                    await _versionRepository.GetByHashAsync(
                        document.Id,
                        contentHash,
                        cancellationToken);

                if (concurrent is not null)
                {
                    if (document.CurrentVersionId != concurrent.Id)
                        document.SetCurrentVersion(concurrent.Id);

                    await _versionRepository.SaveChangesAsync(
                        cancellationToken);

                    unchangedArtifacts++;
                    continue;
                }

                await _storage.DeleteAsync(
                    workspaceId.ToString(),
                    storageKey,
                    CancellationToken.None);

                throw new InvalidOperationException(
                    $"Git artifact '{artifact.ExternalIdentifier}' conflicted with another ingestion request.");
            }

            // The version now exists in the database, so it is safe to
            // update Document.CurrentVersionId to point to it.
            document.SetCurrentVersion(version.Id);

            await _documentRepository.SaveChangesAsync(
                cancellationToken);

            createdVersions++;
        }

        var deactivatedDocuments = 0;

        foreach (var document in existingDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!document.IsActive ||
                string.IsNullOrWhiteSpace(document.ExternalIdentifier))
            {
                continue;
            }

            if (seenExternalIdentifiers.Contains(
                    document.ExternalIdentifier))
            {
                continue;
            }

            document.Deactivate();

            deactivatedDocuments++;
        }

        if (deactivatedDocuments > 0)
        {
            await _versionRepository.SaveChangesAsync(
                cancellationToken);
        }

        return new GitReconciliationResult(
            workspaceId,
            knowledgeSourceId,
            snapshot.Revision,
            createdDocuments,
            createdVersions,
            unchangedArtifacts,
            reactivatedDocuments,
            deactivatedDocuments,
            skippedArtifacts);
    }
}
