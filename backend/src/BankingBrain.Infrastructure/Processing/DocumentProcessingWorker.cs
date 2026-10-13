using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Domain.Entities.DocumentSection;
using BankingBrain.Domain.Entities.ProcessingAttempt;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Diagnostics;

namespace BankingBrain.Infrastructure.Processing;

public sealed class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingWorker> _logger;
    private readonly ProcessingOptions _options;

    public DocumentProcessingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingWorker> logger,
        IOptions<ProcessingOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Honors the configured poll interval (previously hardcoded to 250ms, silently
        // ignoring ProcessingOptions.PollMilliseconds) and only waits on the timer when no
        // job was available - when a job WAS claimed and processed, the next claim attempt
        // starts immediately instead of idling out the interval, so a backlog drains at the
        // speed work actually completes rather than being capped at one job per tick. Claim/
        // process/complete/fail/lease semantics below are unchanged.
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, _options.PollMilliseconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            using var claimScope = _scopeFactory.CreateScope();
            var jobs = claimScope.ServiceProvider.GetRequiredService<
                BankingBrain.Application.Features.Documents.Interfaces.IProcessingJobRepository>();
            var job = await jobs.ClaimNextAsync(
                DateTime.UtcNow,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                stoppingToken);
            if (job is null)
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                continue;
            }
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Document processing failed for version {DocumentVersionId}",
                    job.DocumentVersionId);
            }
        }
    }

    private async Task ProcessAsync(
        BankingBrain.Domain.Entities.ProcessingJob.ProcessingJob job,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<
            BankingBrain.Application.Features.Documents.Interfaces.IDocumentVersionRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var processors = scope.ServiceProvider.GetServices<IDocumentProcessor>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<
            BankingBrain.Application.Features.Documents.Interfaces.IProcessingJobRepository>();

        if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken!.Value, cancellationToken))
            return;

        var version = await dbContext.DocumentVersions
            .FirstOrDefaultAsync(x => x.Id == job.DocumentVersionId, cancellationToken);
        if (version is null ||
            version.ProcessingState == Domain.Enums.DocumentProcessingState.Processed)
            return;

        if (version.ProcessingState == Domain.Enums.DocumentProcessingState.Processing)
        {
            version.RecoverInterruptedProcessing();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        version.BeginProcessing();
        var document = await dbContext.Documents
            .AsNoTracking()
            .FirstAsync(x => x.Id == version.DocumentId, cancellationToken);
        var signature = await DocumentSignatureReader.ReadAsync(
            storage,
            document.WorkspaceId.ToString(),
            version.StorageKey,
            cancellationToken);
        var processor = processors.FirstOrDefault(x =>
            x.CanProcess(version.MediaType, version.FileName, signature));
        _logger.LogInformation(
            "Selected document processor {Processor} version {ProcessorVersion} for version {DocumentVersionId}",
            processor?.Identity ?? "unsupported",
            processor?.Version ?? "none",
            version.Id);
        var attempt = ProcessingAttempt.Start(
            version.Id,
            await repository.GetNextAttemptNumberAsync(version.Id, cancellationToken),
            processor?.Identity);
        await repository.AddAttemptAsync(attempt, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsAttemptConflict(exception))
        {
            return;
        }

        try
        {
            var processingTimer = Stopwatch.StartNew();
            await using var content = await storage.OpenReadAsync(
                document.WorkspaceId.ToString(),
                version.StorageKey,
                cancellationToken);
            if (processor is null)
                throw new InvalidOperationException(
                    $"No processor supports {version.MediaType}.");

            var previousSections = await dbContext.DocumentSections
                .Where(section => section.DocumentVersionId == version.Id)
                .ToListAsync(cancellationToken);
            dbContext.DocumentSections.RemoveRange(previousSections);
            var normalized = await processor.ProcessAsync(
                new DocumentProcessingInput(version.MediaType, version.FileName, content),
                cancellationToken);
            processingTimer.Stop();
            _logger.LogInformation(
                "Document processor {Processor} version {ProcessorVersion} extracted {ItemCount} items in {ElapsedMilliseconds} ms for version {DocumentVersionId}",
                processor.Identity,
                processor.Version,
                normalized.Sections.Count,
                processingTimer.ElapsedMilliseconds,
                version.Id);

            if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken!.Value, cancellationToken))
                return;

            foreach (var section in normalized.Sections)
            {
                dbContext.DocumentSections.Add(DocumentSection.Create(
                    version.Id,
                    section.Sequence,
                    section.Kind,
                    section.Title,
                    section.Text,
                    section.PageNumber,
                    section.SourceLocation,
                    section.ParentSequence,
                    section.ContentType,
                    section.Value,
                    section.SlideNumber,
                    section.SheetName,
                    section.RowNumber,
                    section.ColumnNumber,
                    section.CellReference));
            }

            version.MarkProcessed(processor.Version);
            attempt.Complete(true, processor.Version, null);
            await repository.SaveChangesAsync(cancellationToken);
            await jobs.CompleteAsync(job.Id, job.LeaseToken!.Value, cancellationToken);

            // SQL Lineage reads each file's content from the DocumentSection text just
            // written above, so it can only be queued once that text actually exists -
            // unlike Code Intelligence (which re-reads the git working copy directly and
            // is queued at snapshot time instead). Queuing it here, right after this
            // version is marked Processed, is the only point in the pipeline where that is
            // guaranteed true.
            if (string.Equals(document.MimeType, "application/sql", StringComparison.OrdinalIgnoreCase))
            {
                var sqlLineageRefreshQueue = scope.ServiceProvider.GetRequiredService<
                    BankingBrain.Application.Common.SqlLineage.ISqlLineageRefreshQueue>();
                await sqlLineageRefreshQueue.QueueRefreshAsync(
                    document.WorkspaceId,
                    document.KnowledgeSourceId,
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            if (!await jobs.OwnsLeaseAsync(job.Id, job.LeaseToken!.Value, cancellationToken))
                return;

            attempt.Complete(false, processor?.Version, exception.Message);
            var retryable = processor is not null && exception is not InvalidDataException;
            var retryScheduled = retryable && job.Attempts < Math.Max(1, _options.MaxAttempts);
            if (retryScheduled)
                version.RecoverInterruptedProcessing();
            else
                version.MarkFailed(exception.Message, processor?.Version);
            await repository.SaveChangesAsync(cancellationToken);
            var jobRetryScheduled = await jobs
                .FailAsync(
                    job.Id,
                    job.LeaseToken!.Value,
                    exception.Message,
                    DateTime.UtcNow.AddSeconds(Math.Max(1, _options.RetryDelaySeconds)),
                    retryable,
                    Math.Max(1, _options.MaxAttempts),
                    cancellationToken);
            if (retryScheduled != jobRetryScheduled && version.ProcessingState == Domain.Enums.DocumentProcessingState.Processing)
            {
                version.MarkFailed(exception.Message, processor?.Version);
                await repository.SaveChangesAsync(cancellationToken);
            }
            _logger.LogError(exception, "Document processing failed for version {DocumentVersionId}", job.DocumentVersionId);
        }
    }

    private static bool IsAttemptConflict(DbUpdateException exception)
    {
        var postgresException = exception.InnerException as PostgresException;
        return postgresException?.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgresException.ConstraintName == "IX_ProcessingAttempts_DocumentVersionId_AttemptNumber";
    }
}