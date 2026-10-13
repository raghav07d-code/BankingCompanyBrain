using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Application.Common.CodeIntelligence;
using BankingBrain.Application.Common.Connectors.Reconciliation;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Domain.Entities.Refresh;
using MediatR;

namespace BankingBrain.Application.Common.Connectors.SnapshotKnowledgeSource;

public sealed class SnapshotKnowledgeSourceCommandHandler
    : IRequestHandler<
        SnapshotKnowledgeSourceCommand,
        Result<SourceSnapshot>>
{
    // Matches CodeRepositorySemanticExtractionService.ExtractorIdentity / ApiIntelligenceExtractionService.ExtractorIdentity
    // (Infrastructure). Kept as plain strings here rather than a cross-layer reference, consistent with how every other
    // extractor identity is already treated as a plain string throughout the semantic model.
    private const string CodeIntelligenceExtractorIdentity = "roslyn-csharp";
    private const string ApiIntelligenceExtractorIdentity = "api-intelligence";

    private readonly KnowledgeSourceSnapshotService _snapshotService;
    private readonly GitSnapshotReconciliationService _reconciliationService;
    private readonly IImmutableRepositorySnapshotWriter _snapshotWriter;
    private readonly IKnowledgeSourceRepository _knowledgeSourceRepository;
    private readonly IKnowledgeSourceRefreshRunRepository _refreshRunRepository;
    private readonly ICodeIntelligenceRefreshQueue _codeIntelligenceRefreshQueue;
    private readonly IApiIntelligenceRefreshQueue _apiIntelligenceRefreshQueue;

    public SnapshotKnowledgeSourceCommandHandler(
        KnowledgeSourceSnapshotService snapshotService,
        GitSnapshotReconciliationService reconciliationService,
        IImmutableRepositorySnapshotWriter snapshotWriter,
        IKnowledgeSourceRepository knowledgeSourceRepository,
        IKnowledgeSourceRefreshRunRepository refreshRunRepository,
        ICodeIntelligenceRefreshQueue codeIntelligenceRefreshQueue,
        IApiIntelligenceRefreshQueue apiIntelligenceRefreshQueue)
    {
        _snapshotService = snapshotService;
        _reconciliationService = reconciliationService;
        _snapshotWriter = snapshotWriter;
        _knowledgeSourceRepository = knowledgeSourceRepository;
        _refreshRunRepository = refreshRunRepository;
        _codeIntelligenceRefreshQueue = codeIntelligenceRefreshQueue;
        _apiIntelligenceRefreshQueue = apiIntelligenceRefreshQueue;
    }

    public async Task<Result<SourceSnapshot>> Handle(
        SnapshotKnowledgeSourceCommand request,
        CancellationToken cancellationToken)
    {
        SourceSnapshot snapshot;

        try
        {
            snapshot = await _snapshotService.SnapshotAsync(
                request.WorkspaceId,
                request.KnowledgeSourceId,
                request.Revision,
                cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            return Result<SourceSnapshot>.Failure(
                new Error(
                    "KNOWLEDGE_SOURCE.NOT_FOUND",
                    exception.Message));
        }
        catch (DirectoryNotFoundException exception)
        {
            return Result<SourceSnapshot>.Failure(
                new Error(
                    "SOURCE.NOT_FOUND",
                    exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result<SourceSnapshot>.Failure(
                new Error(
                    "SOURCE.CONNECTOR_ERROR",
                    exception.Message));
        }

        if (string.Equals(
                snapshot.ConnectorKey,
                "git",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _reconciliationService.ReconcileAsync(
                    snapshot,
                    request.WorkspaceId,
                    request.KnowledgeSourceId,
                    cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Result<SourceSnapshot>.Failure(
                    new Error(
                        "SOURCE.RECONCILIATION_ERROR",
                        exception.Message));
            }

            if (string.IsNullOrWhiteSpace(snapshot.Revision))
            {
                return Result<SourceSnapshot>.Failure(
                    new Error(
                        "SOURCE.RECONCILIATION_ERROR",
                        "Git snapshot revision is required to bind extraction to an immutable repository copy."));
            }

            // Writes the already-in-memory artifact content (read once, before this point,
            // by _snapshotService.SnapshotAsync) to an immutable, revision-keyed directory.
            // Nothing queued below - or run on demand later - will ever read the connector's
            // live working copy again for this revision.
            await _snapshotWriter.WriteAsync(
                request.KnowledgeSourceId,
                snapshot,
                cancellationToken);

            await _knowledgeSourceRepository.SetLastSnapshotRevisionAsync(
                request.WorkspaceId,
                request.KnowledgeSourceId,
                snapshot.Revision,
                cancellationToken);

            // Code Intelligence refresh is queued for background processing (Roslyn
            // analysis is long-running) and only after reconciliation has succeeded, so a
            // failed reconciliation never triggers a semantic refresh. The queued run is
            // bound to this exact revision; any still-active run queued by an earlier
            // snapshot for this same (workspace, knowledge source, extractor) is
            // superseded so an older, slower run can never be mistaken for "the" result of
            // a newer snapshot.
            await QueueRefreshAsync(
                request.WorkspaceId,
                request.KnowledgeSourceId,
                snapshot.Revision,
                CodeIntelligenceExtractorIdentity,
                _codeIntelligenceRefreshQueue.QueueRefreshAsync,
                cancellationToken);

            // API Intelligence is bound to the same immutable revision the same way - no
            // DocumentSection dependency, so no processing-pipeline race, and it is safe to
            // queue here at snapshot time alongside Code Intelligence.
            await QueueRefreshAsync(
                request.WorkspaceId,
                request.KnowledgeSourceId,
                snapshot.Revision,
                ApiIntelligenceExtractorIdentity,
                _apiIntelligenceRefreshQueue.QueueRefreshAsync,
                cancellationToken);

            // Data Access Intelligence is deliberately NOT auto-queued here. Measured
            // empirically: a third independent full-repository Roslyn compilation running
            // in parallel with Code Intelligence's and API Intelligence's own (all three
            // triggered by the same snapshot) delays Code Intelligence's completion enough
            // to matter under load. It remains available on demand via its own explicit
            // extract endpoint, which (like the other two) now also resolves its input
            // from the knowledge source's last immutable snapshot revision rather than the
            // connector's live working copy.

            // Unlike Code Intelligence (which re-reads files from the immutable snapshot
            // directly, independent of the Document processing pipeline), SQL Lineage reads
            // each file's content from its DocumentSection text, which only exists once
            // DocumentProcessingWorker has finished processing that version. Queuing a SQL
            // lineage refresh here, immediately after reconciliation, would race that worker
            // and silently skip not-yet-processed files. SQL lineage is instead queued from
            // DocumentProcessingWorker itself, right after it marks a .sql version Processed
            // - see the call there for the full reasoning. It was never exposed to the
            // disk-mutation race this change fixes, since DocumentVersion content is already
            // immutable once persisted.
        }

        return Result<SourceSnapshot>.Success(snapshot);
    }

    private async Task QueueRefreshAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        string revision,
        string extractorIdentity,
        Func<Guid, CancellationToken, ValueTask> enqueue,
        CancellationToken cancellationToken)
    {
        var run = KnowledgeSourceRefreshRun.Queue(workspaceId, knowledgeSourceId, extractorIdentity, revision);
        await _refreshRunRepository.AddAsync(run, cancellationToken);
        await _refreshRunRepository.SaveChangesAsync(cancellationToken);

        await _refreshRunRepository.SupersedeActiveAsync(
            workspaceId,
            knowledgeSourceId,
            extractorIdentity,
            excludingRunId: run.Id,
            cancellationToken);

        await enqueue(run.Id, cancellationToken);
    }
}
