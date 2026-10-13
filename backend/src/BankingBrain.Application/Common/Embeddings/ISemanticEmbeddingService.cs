namespace BankingBrain.Application.Common.Embeddings;

public interface ISemanticEmbeddingService
{
    Task IndexAsync(Guid workspaceId, IReadOnlyList<SemanticEmbeddingDocument> documents, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticEmbeddingSearchResult>> SearchAsync(Guid workspaceId, SemanticEmbeddingSearchRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Removes embeddings for sources that are no longer current (e.g. code entities whose
    /// owning file was deleted/deactivated), so search results stop returning them without
    /// requiring a full workspace reindex.
    /// </summary>
    Task DeleteAsync(Guid workspaceId, string sourceType, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken);
}