namespace BankingBrain.Application.Common.Embeddings;

public sealed record EmbeddingVector(
    IReadOnlyList<float> Values,
    string ProviderIdentity,
    string ProviderVersion,
    string Model);

public sealed record SemanticEmbeddingDocument(
    string SourceType,
    Guid SourceId,
    Guid? DocumentVersionId,
    Guid? DocumentSectionId,
    Guid? SemanticEntityId,
    Guid? SemanticClaimId,
    Guid? EvidenceId,
    string Content,
    string ContentHash,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SemanticEmbeddingFilter(
    Guid? DocumentVersionId = null,
    string? SourceType = null,
    Guid? SourceId = null,
    string? ProviderIdentity = null,
    string? ProviderVersion = null,
    string? Model = null);

public sealed record SemanticEmbeddingSearchRequest(
    string Query,
    int Limit = 10,
    SemanticEmbeddingFilter? Filter = null);

public sealed record SemanticEmbeddingSearchResult(
    Guid EmbeddingId,
    string SourceType,
    Guid SourceId,
    Guid? DocumentVersionId,
    Guid? DocumentSectionId,
    Guid? SemanticEntityId,
    Guid? SemanticClaimId,
    Guid? EvidenceId,
    double Similarity,
    string ProviderIdentity,
    string ProviderVersion,
    string Model,
    IReadOnlyDictionary<string, string> Metadata);