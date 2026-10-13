namespace BankingBrain.Application.Common.CodeIntelligence;

/// <summary>
/// Queues a reference to an already-created <c>KnowledgeSourceRefreshRun</c> - never a bare
/// (workspace, knowledge source) pair - so the worker that eventually dequeues it resolves
/// the exact immutable revision that run was bound to at queue time, instead of whatever
/// the connector's working copy happens to contain when it gets around to running.
/// </summary>
public interface ICodeIntelligenceRefreshQueue
{
    ValueTask QueueRefreshAsync(
        Guid refreshRunId,
        CancellationToken cancellationToken);
}
