using System.Threading.Channels;
using BankingBrain.Application.Common.CodeIntelligence;

namespace BankingBrain.Infrastructure.CodeIntelligence;

public sealed class InMemoryCodeIntelligenceRefreshQueue : ICodeIntelligenceRefreshQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask QueueRefreshAsync(
        Guid refreshRunId,
        CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(refreshRunId, cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
