using System.Threading.Channels;
using BankingBrain.Application.Common.SqlLineage;

namespace BankingBrain.Infrastructure.SqlLineage;

public sealed class InMemorySqlLineageRefreshQueue : ISqlLineageRefreshQueue
{
    private readonly Channel<(Guid WorkspaceId, Guid KnowledgeSourceId)> _channel =
        Channel.CreateUnbounded<(Guid WorkspaceId, Guid KnowledgeSourceId)>();

    public ValueTask QueueRefreshAsync(
        Guid workspaceId,
        Guid knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(
            (workspaceId, knowledgeSourceId),
            cancellationToken);
    }

    public IAsyncEnumerable<(Guid WorkspaceId, Guid KnowledgeSourceId)> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
