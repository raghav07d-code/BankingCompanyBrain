namespace BankingBrain.Application.Common.Connectors;

public sealed record SourceConnectorRequest(
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string ConnectorKey,
    string ExternalIdentifier,
    string? Revision = null);

public sealed record SourceSnapshot(
    string ConnectorKey,
    string ExternalIdentifier,
    string? Revision,
    DateTime CapturedOnUtc,
    IReadOnlyCollection<SourceArtifact> Artifacts);

public sealed record SourceArtifact(
    string ExternalIdentifier,
    string Name,
    SourceArtifactKind Kind,
    string? Content,
    string? MediaType,
    string? Path,
    string? Revision);