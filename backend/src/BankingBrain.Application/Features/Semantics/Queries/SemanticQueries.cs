using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Semantics.Queries;

public sealed record GetSemanticRunsQuery(Guid WorkspaceId, Guid VersionId) : IQuery<IReadOnlyList<SemanticRunResponse>>;
public sealed record GetSemanticEntitiesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<SemanticEntityResponse>>;
public sealed record GetSemanticClaimsQuery(Guid WorkspaceId, Guid? VersionId, Guid? EntityId = null, string? Predicate = null, DateTime? AsOfUtc = null, DateTime? ValidAtUtc = null) : IQuery<IReadOnlyList<SemanticClaimResponse>>;
public sealed record GetSemanticClaimQuery(Guid WorkspaceId, Guid ClaimId) : IQuery<SemanticClaimDetailsResponse>;
public sealed record GetSemanticEntityTypesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<SemanticEntityTypeResponse>>;
public sealed record GetSemanticPredicatesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<SemanticPredicateResponse>>;
