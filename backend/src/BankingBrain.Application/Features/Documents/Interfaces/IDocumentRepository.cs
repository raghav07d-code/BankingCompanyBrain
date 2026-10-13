using BankingBrain.Domain.Entities.Document;

namespace BankingBrain.Application.Features.Documents.Interfaces;

public interface IDocumentRepository
{
    Task AddAsync(
        Document document,
        CancellationToken cancellationToken);

    Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> GetByWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken);

    Task<Document?> GetByIdAsync(
        Guid workspaceId,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<Document?> GetByExternalIdentifierAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string externalIdentifier,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> GetByKnowledgeSourceAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken);
}