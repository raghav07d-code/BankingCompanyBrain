using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Domain.Enums;
using BankingBrain.Infrastructure.CodeIntelligence;
using BankingBrain.Infrastructure.Persistence.Context;
using BankingBrain.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankingBrain.Infrastructure.Processing;

public sealed class CodeIntelligenceRefreshWorker : BackgroundService
{
    private readonly InMemoryCodeIntelligenceRefreshQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CodeIntelligenceRefreshWorker> _logger;

    public CodeIntelligenceRefreshWorker(
        InMemoryCodeIntelligenceRefreshQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CodeIntelligenceRefreshWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var refreshRunId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var runRepository = scope.ServiceProvider
                    .GetRequiredService<IKnowledgeSourceRefreshRunRepository>();

                var run = await runRepository.GetByIdAsync(refreshRunId, stoppingToken);

                // Already superseded by a newer request for the same (workspace,
                // knowledge source, extractor) tuple before this one even started -
                // nothing to do; the newer run's own queued item will do the real work.
                if (run is null || run.Status == KnowledgeSourceRefreshRunStatus.Superseded)
                    continue;

                run.MarkRunning();
                await runRepository.SaveChangesAsync(stoppingToken);

                try
                {
                    var extractionService = scope.ServiceProvider
                        .GetRequiredService<ICodeRepositorySemanticExtractionService>();

                    await extractionService.ExtractAsync(
                        run.WorkspaceId,
                        run.KnowledgeSourceId,
                        run.Revision,
                        stoppingToken);

                    // Extraction completed without throwing, so it is treated as
                    // successful and only then do we refresh the embedding index and
                    // graph projection for the workspace. The run is marked Completed
                    // only after this finishes too - a caller observing Completed must
                    // be able to trust that entities, relationships, embeddings, and the
                    // graph projection are all consistent for this revision, not just that
                    // Roslyn analysis happened to return.
                    await RefreshEmbeddingsAndGraphAsync(
                        scope,
                        run.WorkspaceId,
                        stoppingToken);

                    run.Complete();
                    await runRepository.SaveChangesAsync(stoppingToken);
                }
                catch (Exception extractionException)
                {
                    run.Fail("code-intelligence-refresh-error", extractionException.Message);
                    await runRepository.SaveChangesAsync(stoppingToken);
                    throw;
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Code intelligence refresh failed for run {RefreshRunId}.",
                    refreshRunId);
            }
        }
    }

    /// <summary>
    /// Refreshes only the affected code semantics rather than re-embedding/rebuilding the
    /// whole workspace: "current" code entities are exactly those participating in at least
    /// one current (active-file, current-version) SemanticRelationship - the same currency
    /// definition the earlier current-vs-historical query filtering uses. Diffing that set
    /// against what is already embedded yields precisely the entities that need a fresh
    /// embedding (new/now-current) and the ones whose embedding must be removed (no longer
    /// current); everything already embedded and still current is left untouched. The graph
    /// projection is still delegated to the existing (now internally diff-based)
    /// ISemanticGraphService.RebuildAsync.
    /// </summary>
    private async Task RefreshEmbeddingsAndGraphAsync(
        IServiceScope scope,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var embeddings = scope.ServiceProvider.GetRequiredService<ISemanticEmbeddingService>();
            var graph = scope.ServiceProvider.GetRequiredService<ISemanticGraphService>();

            var currentEntityIds = (await CurrentDocumentVersionFilter
                    .CurrentEntityIds(db, workspaceId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var embeddedEntityIds = (await db.SemanticEmbeddings
                    .Where(embedding => embedding.WorkspaceId == workspaceId && embedding.SourceType == "SEMANTIC_ENTITY")
                    .Select(embedding => embedding.SourceId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var entityIdsToEmbed = currentEntityIds.Except(embeddedEntityIds).ToArray();
            var entityIdsToRemove = embeddedEntityIds.Except(currentEntityIds).ToArray();

            if (entityIdsToEmbed.Length > 0)
            {
                var entitiesToEmbed = await db.SemanticEntities
                    .AsNoTracking()
                    .Where(entity => entity.WorkspaceId == workspaceId && entityIdsToEmbed.Contains(entity.Id))
                    .ToListAsync(cancellationToken);

                var documents = entitiesToEmbed
                    .Select(entity => new SemanticEmbeddingDocument(
                        "SEMANTIC_ENTITY",
                        entity.Id,
                        null,
                        null,
                        entity.Id,
                        null,
                        null,
                        $"{entity.EntityType} {entity.CanonicalName} {entity.Aliases}",
                        SemanticEmbeddingService.HashContent(
                            $"{entity.EntityType}|{entity.CanonicalName}|{entity.Aliases}"),
                        new Dictionary<string, string>
                        {
                            ["semantic.entityType"] = entity.EntityType
                        }))
                    .ToList();

                await embeddings.IndexAsync(workspaceId, documents, cancellationToken);
            }

            if (entityIdsToRemove.Length > 0)
            {
                await embeddings.DeleteAsync(workspaceId, "SEMANTIC_ENTITY", entityIdsToRemove, cancellationToken);
            }

            await graph.RebuildAsync(workspaceId, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Embedding/graph refresh failed for workspace {WorkspaceId} after a successful code intelligence extraction.",
                workspaceId);
        }
    }
}
