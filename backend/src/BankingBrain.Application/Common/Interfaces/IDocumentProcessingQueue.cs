namespace BankingBrain.Application.Common.Interfaces;

public interface IDocumentProcessingQueue
{
    ValueTask EnqueueAsync(Guid documentVersionId, CancellationToken cancellationToken);
}