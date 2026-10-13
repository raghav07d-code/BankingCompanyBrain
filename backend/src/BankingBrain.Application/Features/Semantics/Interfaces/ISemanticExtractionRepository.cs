using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Application.Common.Models;
using BankingBrain.Domain.Entities.Semantics;
using BankingBrain.Domain.Enums;

namespace BankingBrain.Application.Features.Semantics.Interfaces;

public sealed record SemanticSource(
    Guid WorkspaceId,
    Guid DocumentVersionId,
    DocumentProcessingState ProcessingState,
    IReadOnlyList<NormalizedSection> Sections);

public interface ISemanticExtractionRepository
{
    Task<SemanticSource?> GetSourceAsync(Guid workspaceId, Guid documentId, Guid versionId, CancellationToken cancellationToken);
    Task<SemanticSource?> GetSourceByVersionAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken);
    Task<SemanticExtractionRun?> GetSuccessfulAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken);
    Task<SemanticExtractionRun?> GetActiveAsync(Guid workspaceId, Guid versionId, string identity, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Batch form of <see cref="GetSuccessfulAsync"/>: which of the given DocumentVersionIds
    /// already have a Succeeded run for this extractor, in one round trip. Used to skip
    /// re-analyzing unchanged files up front, rather than discovering the skip only after
    /// paying for the analysis.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetExtractedVersionIdsAsync(Guid workspaceId, string identity, string version, IReadOnlyCollection<Guid> candidateVersionIds, CancellationToken cancellationToken);
    Task<bool> VersionBelongsToWorkspaceAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken);
    Task<SemanticExtractionRun> AddRunAsync(SemanticExtractionRun run, CancellationToken cancellationToken);
    Task<bool> TryQueueAsync(SemanticExtractionRun run, SemanticProcessingJob job, CancellationToken cancellationToken);
    Task PersistAsync(SemanticExtractionRun run, SemanticExtractionResult result, SemanticSource source, CancellationToken cancellationToken);
    Task FailAsync(SemanticExtractionRun run, string category, string error, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticExtractionRun>> GetRunsAsync(Guid workspaceId, Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticEntity>> GetEntitiesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticClaim>> GetClaimsAsync(Guid workspaceId, Guid? versionId, Guid? entityId, string? predicate, DateTime? asOfUtc, DateTime? validAtUtc, CancellationToken cancellationToken);
    Task<SemanticClaimDetails?> GetClaimAsync(Guid workspaceId, Guid claimId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists Code Intelligence observations directly onto the existing semantic
    /// entity/relationship/evidence model. Unlike <see cref="PersistAsync"/>, this does not
    /// assume a single DocumentVersion: entities are ensured workspace-wide first (so
    /// cross-file/cross-project relationships resolve regardless of file processing order),
    /// then relationships are persisted per DocumentVersion, each under its own
    /// SemanticExtractionRun using the same (workspace, version, extractor identity/version)
    /// idempotency semantics as the rest of the pipeline.
    /// </summary>
    Task<CodeSemanticPersistSummary> PersistCodeObservationsAsync(
        Guid workspaceId,
        string extractorIdentity,
        string extractorVersion,
        IReadOnlyList<CodeEntityObservation> entities,
        IReadOnlyList<CodeFileRelationshipBatch> fileRelationships,
        CancellationToken cancellationToken);
}

public sealed record SemanticClaimDetails(
    SemanticClaim Claim,
    SemanticEvidence Evidence,
    SemanticEntity? SubjectEntity,
    SemanticEntity? ObjectEntity);
