using BankingBrain.Application.Common.SqlLineage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankingBrain.Infrastructure.SqlLineage;

/// <summary>Drains SQL Lineage refresh requests queued after a successful Git snapshot reconciliation, mirroring <c>CodeIntelligenceRefreshWorker</c>'s shape and failure handling.</summary>
public sealed class SqlLineageRefreshWorker : BackgroundService
{
    private readonly InMemorySqlLineageRefreshQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlLineageRefreshWorker> _logger;

    public SqlLineageRefreshWorker(
        InMemorySqlLineageRefreshQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SqlLineageRefreshWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (workspaceId, knowledgeSourceId) in
            _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var extractionService = scope.ServiceProvider
                    .GetRequiredService<ISqlLineageExtractionService>();

                await extractionService.ExtractAsync(
                    workspaceId,
                    knowledgeSourceId,
                    stoppingToken);
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
                    "SQL lineage refresh failed for workspace {WorkspaceId}, knowledge source {KnowledgeSourceId}.",
                    workspaceId,
                    knowledgeSourceId);
            }
        }
    }
}
