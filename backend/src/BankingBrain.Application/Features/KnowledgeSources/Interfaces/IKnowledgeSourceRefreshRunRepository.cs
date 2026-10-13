using BankingBrain.Domain.Entities.Refresh;

namespace BankingBrain.Application.Features.KnowledgeSources.Interfaces;

public interface IKnowledgeSourceRefreshRunRepository
{
    Task AddAsync(KnowledgeSourceRefreshRun run, CancellationToken cancellationToken);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);

    Task<KnowledgeSourceRefreshRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken);

    Task<KnowledgeSourceRefreshRun?> GetLatestAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string extractorIdentity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks every still-active (Queued or Running) run for this (workspace, knowledge
    /// source, extractor) tuple, other than <paramref name="excludingRunId"/>, as
    /// Superseded - the ordering guarantee that a newer snapshot's refresh request always
    /// wins over an older one still in flight for the same tuple.
    /// </summary>
    Task SupersedeActiveAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string extractorIdentity,
        Guid excludingRunId,
        CancellationToken cancellationToken);
}
