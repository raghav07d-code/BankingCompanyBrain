using BankingBrain.Domain.Entities.KnowledgeSource;

namespace BankingBrain.Application.Features.KnowledgeSources.Interfaces;

public interface IKnowledgeSourceRepository
{
    Task<bool> ExistsAsync(
        Guid workspaceId,
        string name,
        CancellationToken cancellationToken);

    Task AddAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken);

    Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeSource>> GetByWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken);

    Task<KnowledgeSource?> GetByIdAsync(
        Guid workspaceId,
        Guid sourceId,
        CancellationToken cancellationToken);

    /// <summary>Records the exact Git revision an immutable snapshot directory now exists for, so later extraction (auto-triggered or on-demand) can resolve the correct, stable repository root without depending on the connector's live working copy.</summary>
    Task SetLastSnapshotRevisionAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken);
}