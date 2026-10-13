using System.Threading.Channels;
using BankingBrain.Application.Common.Interfaces;

namespace BankingBrain.Infrastructure.Processing;

public sealed class InMemoryDocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(
        Guid documentVersionId,
        CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(documentVersionId, cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}