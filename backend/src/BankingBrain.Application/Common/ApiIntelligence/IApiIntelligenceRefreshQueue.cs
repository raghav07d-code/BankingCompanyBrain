namespace BankingBrain.Application.Common.ApiIntelligence;

/// <summary>Mirrors <c>ICodeIntelligenceRefreshQueue</c> exactly: queues a reference to an already-created <c>KnowledgeSourceRefreshRun</c>, bound to its exact immutable revision at queue time.</summary>
public interface IApiIntelligenceRefreshQueue
{
    ValueTask QueueRefreshAsync(
        Guid refreshRunId,
        CancellationToken cancellationToken);
}
