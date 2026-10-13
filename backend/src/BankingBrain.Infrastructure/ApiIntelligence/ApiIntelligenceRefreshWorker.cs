using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankingBrain.Infrastructure.ApiIntelligence;

/// <summary>Drains API Intelligence refresh requests queued at snapshot time, mirroring <c>CodeIntelligenceRefreshWorker</c>'s run-tracking/state-transition logic exactly.</summary>
public sealed class ApiIntelligenceRefreshWorker : BackgroundService
{
    private readonly InMemoryApiIntelligenceRefreshQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiIntelligenceRefreshWorker> _logger;

    public ApiIntelligenceRefreshWorker(
        InMemoryApiIntelligenceRefreshQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ApiIntelligenceRefreshWorker> logger)
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

                if (run is null || run.Status == KnowledgeSourceRefreshRunStatus.Superseded)
                    continue;

                run.MarkRunning();
                await runRepository.SaveChangesAsync(stoppingToken);

                try
                {
                    var extractionService = scope.ServiceProvider
                        .GetRequiredService<IApiIntelligenceExtractionService>();

                    await extractionService.ExtractAsync(
                        run.WorkspaceId,
                        run.KnowledgeSourceId,
                        run.Revision,
                        stoppingToken);

                    run.Complete();
                    await runRepository.SaveChangesAsync(stoppingToken);
                }
                catch (Exception extractionException)
                {
                    run.Fail("api-intelligence-refresh-error", extractionException.Message);
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
                    "API intelligence refresh failed for run {RefreshRunId}.",
                    refreshRunId);
            }
        }
    }
}
