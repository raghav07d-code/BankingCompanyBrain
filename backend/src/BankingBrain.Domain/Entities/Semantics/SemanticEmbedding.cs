namespace BankingBrain.Domain.Entities.Semantics;

public sealed class SemanticEmbedding
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid? DocumentVersionId { get; private set; }
    public Guid? DocumentSectionId { get; private set; }
    public Guid? SemanticEntityId { get; private set; }
    public Guid? SemanticClaimId { get; private set; }
    public Guid? EvidenceId { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string VectorJson { get; private set; } = string.Empty;
    public int Dimensions { get; private set; }
    public string ProviderIdentity { get; private set; } = string.Empty;
    public string ProviderVersion { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string MetadataJson { get; private set; } = "{}";
    public DateTime CreatedOnUtc { get; private set; }

    private SemanticEmbedding() { }

    public static SemanticEmbedding Create(
        Guid workspaceId,
        string sourceType,
        Guid sourceId,
        Guid? documentVersionId,
        Guid? documentSectionId,
        Guid? semanticEntityId,
        Guid? semanticClaimId,
        Guid? evidenceId,
        string contentHash,
        string content,
        string vectorJson,
        int dimensions,
        string providerIdentity,
        string providerVersion,
        string model,
        string metadataJson)
    {
        if (workspaceId == Guid.Empty || sourceId == Guid.Empty) throw new ArgumentException("Embedding workspace and source identifiers are required.");
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(vectorJson)) throw new ArgumentException("Embedding source, content hash, and vector are required.");
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        if (string.IsNullOrWhiteSpace(providerIdentity) || string.IsNullOrWhiteSpace(providerVersion) || string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Embedding provider metadata is required.");
        return new SemanticEmbedding
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, SourceType = sourceType.Trim().ToUpperInvariant(), SourceId = sourceId,
            DocumentVersionId = documentVersionId, DocumentSectionId = documentSectionId, SemanticEntityId = semanticEntityId,
            SemanticClaimId = semanticClaimId, EvidenceId = evidenceId, ContentHash = contentHash.Trim(), Content = content,
            VectorJson = vectorJson, Dimensions = dimensions, ProviderIdentity = providerIdentity.Trim(), ProviderVersion = providerVersion.Trim(),
            Model = model.Trim(), MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson, CreatedOnUtc = DateTime.UtcNow
        };
    }

    public void ReplaceVector(string contentHash, string content, string vectorJson, int dimensions, string providerIdentity, string providerVersion, string model, string metadataJson, Guid? documentVersionId, Guid? documentSectionId, Guid? semanticEntityId, Guid? semanticClaimId, Guid? evidenceId)
    {
        ContentHash = contentHash.Trim(); Content = content; VectorJson = vectorJson; Dimensions = dimensions;
        ProviderIdentity = providerIdentity.Trim(); ProviderVersion = providerVersion.Trim(); Model = model.Trim(); MetadataJson = metadataJson;
        DocumentVersionId = documentVersionId; DocumentSectionId = documentSectionId; SemanticEntityId = semanticEntityId; SemanticClaimId = semanticClaimId; EvidenceId = evidenceId;
        CreatedOnUtc = DateTime.UtcNow;
    }
}