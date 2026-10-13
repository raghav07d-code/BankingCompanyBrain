using System.Diagnostics;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankingBrain.Infrastructure.Processing;

public sealed class SemanticExtractionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessingOptions _options;
    private readonly ILogger<SemanticExtractionWorker> _logger;

    public SemanticExtractionWorker(IServiceScopeFactory scopeFactory, IOptions<ProcessingOptions> options, ILogger<SemanticExtractionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only waits on the poll timer when no job was available - when a job WAS claimed
        // and processed, the next claim attempt starts immediately instead of idling out
        // the interval, so a backlog drains at the speed work actually completes rather
        // than being capped at one job per tick. Claim/process/complete/fail/lease
        // semantics below are unchanged.
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, _options.PollMilliseconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            using var claimScope = _scopeFactory.CreateScope();
            var jobs = claimScope.ServiceProvider.GetRequiredService<ISemanticProcessingJobRepository>();
            var job = await jobs.ClaimNextAsync(DateTime.UtcNow, TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)), stoppingToken);
            if (job is null)
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                continue;
            }
            try { await ProcessAsync(job, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { _logger.LogError(exception, "Semantic job {JobId} failed outside processing boundary", job.Id); }
        }
    }

    private async Task ProcessAsync(Domain.Entities.Semantics.SemanticProcessingJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<ISemanticProcessingJobRepository>();
        var repository = scope.ServiceProvider.GetRequiredService<ISemanticExtractionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var extractor = scope.ServiceProvider.GetRequiredService<ISemanticExtractor>();
        if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken!.Value, cancellationToken)) return;

        var run = await db.SemanticExtractionRuns.FirstOrDefaultAsync(item => item.Id == job.ExtractionRunId && item.WorkspaceId == job.WorkspaceId, cancellationToken);
        var source = await repository.GetSourceByVersionAsync(job.WorkspaceId, job.DocumentVersionId, cancellationToken);
        if (run is null || source is null || source.ProcessingState != DocumentProcessingState.Processed)
        {
            if (run is not null)
            {
                run.Fail("source-not-ready", "Semantic source is not available for processing.");
                await db.SaveChangesAsync(cancellationToken);
            }
            await jobs.FailAsync(job.Id, job.LeaseToken!.Value, "Semantic source is not available for processing.", DateTime.UtcNow, false, 1, cancellationToken);
            return;
        }

        run.MarkRunning();
        await db.SaveChangesAsync(cancellationToken);
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await extractor.ExtractAsync(new BankingBrain.Application.Common.Models.SemanticExtractionInput(job.WorkspaceId, job.DocumentVersionId, source.Sections), cancellationToken);
            if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken.Value, cancellationToken)) return;
            await repository.PersistAsync(run, result, source, cancellationToken);
            await jobs.CompleteAsync(job.Id, job.LeaseToken.Value, cancellationToken);
            _logger.LogInformation("Semantic job {JobId}, run {RunId}, version {VersionId}, extractor {Extractor} {ExtractorVersion} succeeded in {ElapsedMilliseconds} ms with {EntityCount} entities, {ClaimCount} claims, {RelationshipCount} relationships", job.Id, run.Id, job.DocumentVersionId, extractor.Identity, extractor.Version, timer.ElapsedMilliseconds, result.Entities.Count, result.Claims.Count, result.Relationships.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken.Value, cancellationToken)) return;
            var retryable = exception is not InvalidDataException;
            var retryScheduled = retryable && job.Attempts < Math.Max(1, _options.MaxAttempts);
            if (retryScheduled) run.ResetForRetry();
            else run.Fail(exception is InvalidDataException ? "invalid-content" : "extractor-error", "Semantic extraction failed.");
            await db.SaveChangesAsync(cancellationToken);
            await jobs.FailAsync(job.Id, job.LeaseToken.Value, "Semantic extraction failed.", DateTime.UtcNow.AddSeconds(Math.Max(1, _options.RetryDelaySeconds)), retryable, Math.Max(1, _options.MaxAttempts), cancellationToken);
            _logger.LogError(exception, "Semantic job {JobId}, run {RunId} failed after {Attempts} attempts", job.Id, run.Id, job.Attempts);
        }
    }

}
