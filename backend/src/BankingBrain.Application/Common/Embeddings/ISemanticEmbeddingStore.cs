namespace BankingBrain.Application.Common.Embeddings;

public interface ISemanticEmbeddingStore
{
    Task IndexAsync(Guid workspaceId, IReadOnlyList<SemanticEmbeddingDocument> documents, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticEmbeddingSearchResult>> SearchAsync(Guid workspaceId, EmbeddingVector query, SemanticEmbeddingSearchRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workspaceId, string sourceType, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken);
}