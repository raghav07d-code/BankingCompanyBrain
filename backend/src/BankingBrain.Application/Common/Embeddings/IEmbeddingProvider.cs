namespace BankingBrain.Application.Common.Embeddings;

public interface IEmbeddingProvider
{
    string Identity { get; }
    string Version { get; }
    string Model { get; }
    Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}