using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.Workspace;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WorkspaceRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(
        Guid ownerUserId,
        string name,
        CancellationToken cancellationToken)
    {
        return _dbContext.Workspaces.AnyAsync(
            x =>
                x.OwnerUserId == ownerUserId &&
                x.Name == name,
            cancellationToken);
    }

    public async Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken)
    {
        await _dbContext.Workspaces.AddAsync(
            workspace,
            cancellationToken);
    }

    public async Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (IsWorkspaceNameConflict(exception))
        {
            return false;
        }
    }

    private static bool IsWorkspaceNameConflict(
        DbUpdateException exception)
    {
        var postgresException = exception.InnerException as PostgresException;

        return postgresException?.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgresException.ConstraintName == "IX_Workspaces_OwnerUserId_Name";
    }

    public async Task<IReadOnlyList<Workspace>> GetByOwnerAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Workspaces
            .AsNoTracking()
            .Where(x =>
                x.OwnerUserId == ownerUserId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Workspace?> GetByIdAsync(
        Guid ownerUserId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == workspaceId &&
                     x.OwnerUserId == ownerUserId &&
                     x.IsActive,
                cancellationToken);
    }
}