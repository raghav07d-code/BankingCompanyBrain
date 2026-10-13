namespace BankingBrain.Application.Common.CodeIntelligence.Semantics;

/// <summary>
/// A code concept (repository/project/namespace/type/member) discovered by Code
/// Intelligence, shaped for the existing SemanticEntity model. DocumentVersionId is
/// the file that declares it, when that file is tracked as a Document (null for
/// concepts with no single declaring file, e.g. the repository itself).
/// </summary>
public sealed record CodeEntityObservation(
    Guid? DocumentVersionId,
    string EntityType,
    string CanonicalName,
    string ExternalIdentifier,
    string? SourceLocation);

/// <summary>
/// A relationship between two code entities (resolved by ExternalIdentifier),
/// anchored to the DocumentVersion whose source contains the evidence span.
/// </summary>
public sealed record CodeRelationshipObservation(
    Guid DocumentVersionId,
    string SourceLocation,
    string SubjectExternalIdentifier,
    string ObjectExternalIdentifier,
    string Predicate,
    decimal Confidence);

public sealed record CodeRepositorySemanticObservations(
    IReadOnlyList<CodeEntityObservation> Entities,
    IReadOnlyList<CodeRelationshipObservation> Relationships);

/// <summary>
/// Maps repo-relative, git-normalized file paths (matching Document.ExternalIdentifier,
/// as produced by the Git connector/reconciliation flow) to the current DocumentVersionId
/// for that file, so Code Intelligence observations can be attributed without re-ingesting
/// file content.
/// </summary>
public sealed record CodeFileDocumentIndex(
    string RepositoryRootPath,
    IReadOnlyDictionary<string, Guid> DocumentVersionIdByRelativePath);

/// <summary>
/// Relationship observations grouped by the DocumentVersion whose file is their evidence,
/// matching the one-extraction-run-per-DocumentVersion granularity of the existing
/// semantic extraction pipeline.
/// </summary>
public sealed record CodeFileRelationshipBatch(
    Guid DocumentVersionId,
    IReadOnlyList<CodeRelationshipObservation> Relationships);

public sealed record CodeSemanticPersistSummary(
    int EntitiesInIndex,
    int RelationshipsPersisted,
    int RunsSucceeded,
    int RunsFailed,
    int RunsSkippedIdempotent);
