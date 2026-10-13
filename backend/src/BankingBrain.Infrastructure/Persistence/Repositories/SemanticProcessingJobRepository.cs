using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class SemanticProcessingJobRepository : ISemanticProcessingJobRepository
{
    private readonly ApplicationDbContext _db;
    public SemanticProcessingJobRepository(ApplicationDbContext db) => _db = db;

    public Task<SemanticProcessingJob?> GetActiveAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken) =>
        _db.SemanticProcessingJobs.FirstOrDefaultAsync(job =>
            job.WorkspaceId == workspaceId && job.DocumentVersionId == versionId &&
            job.ExtractorIdentity == identity && job.ExtractorVersion == version &&
            (job.Status == ProcessingJobStatus.Pending || job.Status == ProcessingJobStatus.Processing), cancellationToken);

    public async Task<bool> TryAddAsync(SemanticProcessingJob job, CancellationToken cancellationToken)
    {
        await _db.SemanticProcessingJobs.AddAsync(job, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<SemanticProcessingJob?> ClaimNextAsync(DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var job = await _db.SemanticProcessingJobs.FromSqlInterpolated($"""
            SELECT * FROM "SemanticProcessingJobs"
            WHERE ("Status" = {(int)ProcessingJobStatus.Pending} AND "AvailableAtUtc" <= {nowUtc})
               OR ("Status" = {(int)ProcessingJobStatus.Processing} AND "LeaseUntilUtc" < {nowUtc})
            ORDER BY "AvailableAtUtc", "CreatedOnUtc"
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """).FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        job.Claim(Guid.NewGuid(), nowUtc.Add(lease));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public Task<bool> OwnsLeaseAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken) =>
        _db.SemanticProcessingJobs.AnyAsync(job => job.Id == jobId && job.LeaseToken == leaseToken && job.Status == ProcessingJobStatus.Processing && job.LeaseUntilUtc > DateTime.UtcNow, cancellationToken);

    public async Task CompleteAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken)
    {
        var job = await _db.SemanticProcessingJobs.FirstOrDefaultAsync(item => item.Id == jobId && item.LeaseToken == leaseToken, cancellationToken);
        if (job is null) return;
        job.Complete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> FailAsync(Guid jobId, Guid leaseToken, string error, DateTime retryAtUtc, bool retryable, int maxAttempts, CancellationToken cancellationToken)
    {
        var job = await _db.SemanticProcessingJobs.FirstOrDefaultAsync(item => item.Id == jobId && item.LeaseToken == leaseToken, cancellationToken);
        if (job is null) return false;
        var scheduled = retryable && job.ScheduleRetry(error, retryAtUtc, Math.Max(1, maxAttempts));
        if (!retryable) job.Fail(error);
        await _db.SaveChangesAsync(cancellationToken);
        return scheduled;
    }
}
