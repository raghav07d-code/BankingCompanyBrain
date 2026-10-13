using BankingBrain.Domain.Entities.Workspace;

namespace BankingBrain.Application.Features.Workspaces.Interfaces;

public interface IWorkspaceRepository
{
    Task<bool> ExistsAsync(
        Guid ownerUserId,
        string name,
        CancellationToken cancellationToken);

    Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken);

    Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Workspace>> GetByOwnerAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<Workspace?> GetByIdAsync(
        Guid ownerUserId,
        Guid workspaceId,
        CancellationToken cancellationToken);
}