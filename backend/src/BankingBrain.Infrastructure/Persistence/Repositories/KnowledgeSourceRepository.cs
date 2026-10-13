using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Domain.Entities.KnowledgeSource;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeSourceRepository : IKnowledgeSourceRepository
{
    private readonly ApplicationDbContext _dbContext;

    public KnowledgeSourceRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(
        Guid workspaceId,
        string name,
        CancellationToken cancellationToken)
    {
        return _dbContext.KnowledgeSources.AnyAsync(
            x => x.WorkspaceId == workspaceId && x.Name == name,
            cancellationToken);
    }

    public Task AddAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken)
    {
        return _dbContext.KnowledgeSources.AddAsync(
            source,
            cancellationToken).AsTask();
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
            when (IsDuplicateName(exception))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<KnowledgeSource>> GetByWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.KnowledgeSources
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<KnowledgeSource?> GetByIdAsync(
        Guid workspaceId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        return _dbContext.KnowledgeSources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == sourceId &&
                     x.WorkspaceId == workspaceId &&
                     x.IsActive,
                cancellationToken);
    }

    public async Task SetLastSnapshotRevisionAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        CancellationToken cancellationToken)
    {
        var tracked = await _dbContext.KnowledgeSources
            .FirstOrDefaultAsync(
                x => x.Id == knowledgeSourceId && x.WorkspaceId == workspaceId,
                cancellationToken);

        if (tracked is null)
            return;

        tracked.SetLastSnapshotRevision(revision);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDuplicateName(DbUpdateException exception)
    {
        var postgresException = exception.InnerException as PostgresException;

        return postgresException?.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgresException.ConstraintName == "IX_KnowledgeSources_WorkspaceId_Name";
    }
}