using BankingBrain.Domain.Entities.Document;
using BankingBrain.Domain.Entities.DocumentVersion;

namespace BankingBrain.Application.Features.Documents.Interfaces;

public interface IDocumentVersionRepository
{
    Task<DocumentVersion?> GetByIdAsync(
        Guid workspaceId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<DocumentVersion?> GetTrackedByIdAsync(
        Guid workspaceId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<DocumentVersion?> GetByHashAsync(
        Guid documentId,
        string contentHash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentVersion>> GetByDocumentAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<int> GetNextVersionNumberAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task AddAsync(
        DocumentVersion version,
        CancellationToken cancellationToken);

    Task AddSectionAsync(
        BankingBrain.Domain.Entities.DocumentSection.DocumentSection section,
        CancellationToken cancellationToken);

    Task AddAttemptAsync(
        BankingBrain.Domain.Entities.ProcessingAttempt.ProcessingAttempt attempt,
        CancellationToken cancellationToken);

    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<int> GetNextAttemptNumberAsync(
        Guid versionId,
        CancellationToken cancellationToken);

    Task<Document?> GetDocumentAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BankingBrain.Domain.Entities.ProcessingAttempt.ProcessingAttempt>>
        GetAttemptsAsync(Guid versionId, CancellationToken cancellationToken);
}