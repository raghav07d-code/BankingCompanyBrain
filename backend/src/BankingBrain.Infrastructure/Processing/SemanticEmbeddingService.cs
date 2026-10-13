using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BankingBrain.Application.Common.Embeddings;

namespace BankingBrain.Infrastructure.Processing;

public sealed class SemanticEmbeddingService : ISemanticEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly ISemanticEmbeddingStore _store;

    public SemanticEmbeddingService(IEmbeddingProvider provider, ISemanticEmbeddingStore store) { _provider = provider; _store = store; }

    public async Task IndexAsync(Guid workspaceId, IReadOnlyList<SemanticEmbeddingDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0) return;
        var vectors = await _provider.EmbedAsync(documents.Select(document => document.Content).ToList(), cancellationToken);
        if (vectors.Count != documents.Count) throw new InvalidOperationException("Embedding provider returned an unexpected vector count.");
        var enriched = documents.Zip(vectors).Select(pair =>
        {
            var metadata = pair.First.Metadata.ToDictionary(item => item.Key, item => item.Value);
            metadata["embedding.provider"] = pair.Second.ProviderIdentity;
            metadata["embedding.version"] = pair.Second.ProviderVersion;
            metadata["embedding.model"] = pair.Second.Model;
            metadata["embedding.vector"] = JsonSerializer.Serialize(pair.Second.Values);
            return pair.First with { Metadata = metadata };
        }).ToList();
        await _store.IndexAsync(workspaceId, enriched, cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticEmbeddingSearchResult>> SearchAsync(Guid workspaceId, SemanticEmbeddingSearchRequest request, CancellationToken cancellationToken)
    {
        var vectors = await _provider.EmbedAsync(new[] { request.Query }, cancellationToken);
        return await _store.SearchAsync(workspaceId, vectors[0], request, cancellationToken);
    }

    public Task DeleteAsync(Guid workspaceId, string sourceType, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken) =>
        _store.DeleteAsync(workspaceId, sourceType, sourceIds, cancellationToken);

    public static string HashContent(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}