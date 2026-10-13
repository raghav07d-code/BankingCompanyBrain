using System.Data;
using System.Diagnostics;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Domain.Entities.ProcessingJob;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class ProcessingJobRepository : IProcessingJobRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ProcessingJobRepository> _logger;
    public ProcessingJobRepository(ApplicationDbContext db, ILogger<ProcessingJobRepository> logger) { _db = db; _logger = logger; }

    public Task<ProcessingJob?> GetActiveAsync(Guid versionId, CancellationToken cancellationToken) =>
        _db.ProcessingJobs.FirstOrDefaultAsync(x =>
            x.DocumentVersionId == versionId &&
            (x.Status == ProcessingJobStatus.Pending || x.Status == ProcessingJobStatus.Processing), cancellationToken);

    public Task AddAsync(ProcessingJob job, CancellationToken cancellationToken) =>
        _db.ProcessingJobs.AddAsync(job, cancellationToken).AsTask();

    public async Task<bool> TryAddAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        await AddAsync(job, cancellationToken);
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException postgres &&
                  postgres.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation &&
                  postgres.ConstraintName == "IX_ProcessingJobs_DocumentVersionId")
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> SupersedePendingAsync(Guid documentVersionId, CancellationToken cancellationToken)
    {
        var active = await _db.ProcessingJobs.FirstOrDefaultAsync(x =>
            x.DocumentVersionId == documentVersionId &&
            x.Status == ProcessingJobStatus.Processing,
            cancellationToken);
        if (active is not null)
        {
            if (active.LeaseUntilUtc is null || active.LeaseUntilUtc > DateTime.UtcNow)
                return false;
            active.MarkFailed("Superseded after lease expiration.");
        }

        var job = await _db.ProcessingJobs.FirstOrDefaultAsync(x =>
            x.DocumentVersionId == documentVersionId &&
            x.Status == ProcessingJobStatus.Pending, cancellationToken);
        if (job is null) return true;
        job.MarkFailed("Superseded by explicit reprocessing.");
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public async Task<ProcessingJob?> ClaimNextAsync(DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken)
    {
        // Diagnostic-only: isolates connection-acquisition time, the SKIP LOCKED claim
        // query itself, and the commit, so worker-side contention can be measured instead
        // of assumed. Never logs job/document content - only elapsed milliseconds.
        var connection = _db.Database.GetDbConnection();
        var connectStopwatch = Stopwatch.StartNew();
        if (connection.State != ConnectionState.Open) await _db.Database.OpenConnectionAsync(cancellationToken);
        var connectionAcquireMs = connectStopwatch.Elapsed.TotalMilliseconds;

        var transactionStopwatch = Stopwatch.StartNew();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var beginTransactionMs = transactionStopwatch.Elapsed.TotalMilliseconds;

        var claimStopwatch = Stopwatch.StartNew();
        var job = await _db.ProcessingJobs
            .FromSqlInterpolated($"""
                SELECT * FROM "ProcessingJobs"
                WHERE ("Status" = {(int)ProcessingJobStatus.Pending} AND "AvailableAtUtc" <= {nowUtc})
                   OR ("Status" = {(int)ProcessingJobStatus.Processing} AND "LeaseUntilUtc" < {nowUtc})
                ORDER BY "AvailableAtUtc", "CreatedOnUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .FirstOrDefaultAsync(cancellationToken);
        var claimQueryMs = claimStopwatch.Elapsed.TotalMilliseconds;

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "DbDiagnostic Operation={Operation} ConnectionAcquireMs={ConnectionAcquireMs} BeginTransactionMs={BeginTransactionMs} ClaimQueryMs={ClaimQueryMs} Claimed={Claimed}",
                "ProcessingJob.ClaimNext", connectionAcquireMs, beginTransactionMs, claimQueryMs, false);
            return null;
        }

        var token = Guid.NewGuid();
        job.Claim(token, nowUtc.Add(lease));
        var saveStopwatch = Stopwatch.StartNew();
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var saveAndCommitMs = saveStopwatch.Elapsed.TotalMilliseconds;

        _logger.LogInformation(
            "DbDiagnostic Operation={Operation} ConnectionAcquireMs={ConnectionAcquireMs} BeginTransactionMs={BeginTransactionMs} ClaimQueryMs={ClaimQueryMs} SaveAndCommitMs={SaveAndCommitMs} Claimed={Claimed}",
            "ProcessingJob.ClaimNext", connectionAcquireMs, beginTransactionMs, claimQueryMs, saveAndCommitMs, true);
        return job;
    }

    public async Task CompleteAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken)
    {
        var job = await _db.ProcessingJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.LeaseToken == leaseToken, cancellationToken);
        if (job is null) return;
        job.MarkCompleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> OwnsLeaseAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken) =>
        _db.ProcessingJobs.AnyAsync(
            x => x.Id == jobId &&
                 x.LeaseToken == leaseToken &&
                 x.Status == ProcessingJobStatus.Processing &&
                 x.LeaseUntilUtc > DateTime.UtcNow,
            cancellationToken);

    public async Task<bool> FailAsync(
        Guid jobId,
        Guid leaseToken,
        string error,
        DateTime retryAtUtc,
        bool retryable,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var job = await _db.ProcessingJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.LeaseToken == leaseToken, cancellationToken);
        if (job is null) return false;
        var retryScheduled = retryable && job.ScheduleRetry(
            error,
            retryAtUtc,
            Math.Max(1, maxAttempts));
        if (!retryable)
            job.MarkFailed(error);
        await _db.SaveChangesAsync(cancellationToken);
        return retryScheduled;
    }

}