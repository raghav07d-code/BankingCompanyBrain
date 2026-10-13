using System.Text.Json;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace BankingBrain.Infrastructure.Persistence.Repositories;

public sealed class SemanticEmbeddingStore : ISemanticEmbeddingStore
{
    private readonly ApplicationDbContext _db;

    public SemanticEmbeddingStore(ApplicationDbContext db) => _db = db;

    public async Task IndexAsync(Guid workspaceId, IReadOnlyList<SemanticEmbeddingDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
            return;

        // Batch-load every embedding that could match one of this call's documents in a
        // single round trip, keyed exactly as the per-item lookup used to compare
        // (WorkspaceId + SourceType + SourceId - not the full unique key, matching the
        // existing upsert semantics unchanged), instead of one query per document.
        var sourceIds = documents.Select(document => document.SourceId).Distinct().ToArray();

        // Grouped rather than a straight ToDictionary: the unique index allows more than
        // one row per (SourceType, SourceId) across different provider/version/model
        // combinations, and the original per-item lookup (FirstOrDefaultAsync, ignoring
        // provider/version/model) tolerated that by taking whichever came first - a
        // plain ToDictionary would throw on the very duplicates this is meant to allow.
        var existingByKey = (await _db.SemanticEmbeddings
                .Where(item => item.WorkspaceId == workspaceId && sourceIds.Contains(item.SourceId))
                .ToListAsync(cancellationToken))
            .GroupBy(item => (item.SourceType, item.SourceId))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var document in documents)
        {
            var sourceType = document.SourceType.ToUpper();
            existingByKey.TryGetValue((sourceType, document.SourceId), out var existing);

            var vector = document.Metadata.TryGetValue("embedding.vector", out var vectorJson)
                ? vectorJson
                : throw new InvalidOperationException("Embedding vector metadata is required by the store.");
            var providerIdentity = document.Metadata["embedding.provider"];
            var providerVersion = document.Metadata["embedding.version"];
            var model = document.Metadata["embedding.model"];
            var dimensions = JsonSerializer.Deserialize<float[]>(vector)?.Length ?? 0;
            var metadata = document.Metadata.Where(item => !item.Key.StartsWith("embedding.", StringComparison.Ordinal)).ToDictionary(item => item.Key, item => item.Value);

            if (existing is null)
            {
                var entity = SemanticEmbedding.Create(workspaceId, document.SourceType, document.SourceId, document.DocumentVersionId, document.DocumentSectionId, document.SemanticEntityId, document.SemanticClaimId, document.EvidenceId, document.ContentHash, document.Content, vector, dimensions, providerIdentity, providerVersion, model, JsonSerializer.Serialize(metadata));
                _db.SemanticEmbeddings.Add(entity);
                existingByKey[(sourceType, document.SourceId)] = entity;
            }
            else
            {
                existing.ReplaceVector(document.ContentHash, document.Content, vector, dimensions, providerIdentity, providerVersion, model, JsonSerializer.Serialize(metadata), document.DocumentVersionId, document.DocumentSectionId, document.SemanticEntityId, document.SemanticClaimId, document.EvidenceId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid workspaceId, string sourceType, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0) return;
        var normalizedType = sourceType.ToUpper();
        _db.SemanticEmbeddings.RemoveRange(
            _db.SemanticEmbeddings.Where(item =>
                item.WorkspaceId == workspaceId && item.SourceType == normalizedType && sourceIds.Contains(item.SourceId)));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticEmbeddingSearchResult>> SearchAsync(Guid workspaceId, EmbeddingVector query, SemanticEmbeddingSearchRequest request, CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        var queryable = _db.SemanticEmbeddings.AsNoTracking().Where(item => item.WorkspaceId == workspaceId && item.ProviderIdentity == query.ProviderIdentity && item.ProviderVersion == query.ProviderVersion && item.Model == query.Model);
        if (filter?.DocumentVersionId is not null) queryable = queryable.Where(item => item.DocumentVersionId == filter.DocumentVersionId);
        if (!string.IsNullOrWhiteSpace(filter?.SourceType)) queryable = queryable.Where(item => item.SourceType == filter.SourceType.Trim().ToUpper());
        if (filter?.SourceId is not null) queryable = queryable.Where(item => item.SourceId == filter.SourceId);
        if (!string.IsNullOrWhiteSpace(filter?.ProviderIdentity)) queryable = queryable.Where(item => item.ProviderIdentity == filter.ProviderIdentity);
        if (!string.IsNullOrWhiteSpace(filter?.ProviderVersion)) queryable = queryable.Where(item => item.ProviderVersion == filter.ProviderVersion);
        if (!string.IsNullOrWhiteSpace(filter?.Model)) queryable = queryable.Where(item => item.Model == filter.Model);
        var records = await queryable.ToListAsync(cancellationToken);
        return records.Select(item =>
        {
            var vector = JsonSerializer.Deserialize<float[]>(item.VectorJson) ?? Array.Empty<float>();
            var similarity = Cosine(query.Values, vector);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(item.MetadataJson) ?? new();
            return new SemanticEmbeddingSearchResult(item.Id, item.SourceType, item.SourceId, item.DocumentVersionId, item.DocumentSectionId, item.SemanticEntityId, item.SemanticClaimId, item.EvidenceId, similarity, item.ProviderIdentity, item.ProviderVersion, item.Model, metadata);
        }).OrderByDescending(item => item.Similarity).Take(Math.Clamp(request.Limit, 1, 100)).ToList();
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || left.Count != right.Count) return 0;
        var dot = 0d; var leftMagnitude = 0d; var rightMagnitude = 0d;
        for (var index = 0; index < left.Count; index++) { dot += left[index] * right[index]; leftMagnitude += left[index] * left[index]; rightMagnitude += right[index] * right[index]; }
        return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : dot / Math.Sqrt(leftMagnitude * rightMagnitude);
    }
}