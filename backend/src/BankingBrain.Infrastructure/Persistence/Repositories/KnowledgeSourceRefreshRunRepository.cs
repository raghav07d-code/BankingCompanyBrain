using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Domain.Entities.Refresh;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeSourceRefreshRunRepository : IKnowledgeSourceRefreshRunRepository
{
    private readonly ApplicationDbContext _dbContext;

    public KnowledgeSourceRefreshRunRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(KnowledgeSourceRefreshRun run, CancellationToken cancellationToken)
    {
        await _dbContext.KnowledgeSourceRefreshRuns.AddAsync(run, cancellationToken);
    }

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<KnowledgeSourceRefreshRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken) =>
        _dbContext.KnowledgeSourceRefreshRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);

    public Task<KnowledgeSourceRefreshRun?> GetLatestAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string extractorIdentity,
        CancellationToken cancellationToken) =>
        _dbContext.KnowledgeSourceRefreshRuns
            .AsNoTracking()
            .Where(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == knowledgeSourceId &&
                x.ExtractorIdentity == extractorIdentity)
            .OrderByDescending(x => x.QueuedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SupersedeActiveAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string extractorIdentity,
        Guid excludingRunId,
        CancellationToken cancellationToken)
    {
        var activeRuns = await _dbContext.KnowledgeSourceRefreshRuns
            .Where(x =>
                x.WorkspaceId == workspaceId &&
                x.KnowledgeSourceId == knowledgeSourceId &&
                x.ExtractorIdentity == extractorIdentity &&
                x.Id != excludingRunId &&
                (x.Status == KnowledgeSourceRefreshRunStatus.Queued || x.Status == KnowledgeSourceRefreshRunStatus.Running))
            .ToListAsync(cancellationToken);

        foreach (var run in activeRuns)
        {
            run.Supersede();
        }

        if (activeRuns.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
