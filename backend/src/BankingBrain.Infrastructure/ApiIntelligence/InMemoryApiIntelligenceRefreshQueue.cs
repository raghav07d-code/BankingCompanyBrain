using System.Threading.Channels;
using BankingBrain.Application.Common.ApiIntelligence;

namespace BankingBrain.Infrastructure.ApiIntelligence;

public sealed class InMemoryApiIntelligenceRefreshQueue : IApiIntelligenceRefreshQueue
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
