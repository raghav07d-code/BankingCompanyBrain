using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Semantics.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Domain.Entities.Semantics;

namespace BankingBrain.Application.Features.Semantics.Queries;

public sealed class GetSemanticRunsHandler : IQueryHandler<GetSemanticRunsQuery, IReadOnlyList<SemanticRunResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkspaceRepository _workspaces;
    private readonly ISemanticExtractionRepository _repository;
    public GetSemanticRunsHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticExtractionRepository repository) { _currentUser = currentUser; _workspaces = workspaces; _repository = repository; }
    public async Task<Result<IReadOnlyList<SemanticRunResponse>>> Handle(GetSemanticRunsQuery request, CancellationToken cancellationToken)
    {
        var access = await CheckAccess(request.WorkspaceId, cancellationToken);
        if (access is not null) return Result<IReadOnlyList<SemanticRunResponse>>.Failure(access);
        if (!await _repository.VersionBelongsToWorkspaceAsync(request.WorkspaceId, request.VersionId, cancellationToken))
            return Result<IReadOnlyList<SemanticRunResponse>>.Failure(new Error("DOCUMENT_VERSION.NOT_FOUND", "Document version was not found."));
        var runs = await _repository.GetRunsAsync(request.WorkspaceId, request.VersionId, cancellationToken);
        return Result<IReadOnlyList<SemanticRunResponse>>.Success(runs.Select(ToResponse).ToList());
    }
    private async Task<Error?> CheckAccess(Guid workspaceId, CancellationToken cancellationToken) =>
        !_currentUser.IsAuthenticated ? new Error("AUTH.UNAUTHORIZED", "Authentication is required.") :
        await _workspaces.GetByIdAsync(_currentUser.UserId, workspaceId, cancellationToken) is null ? new Error("WORKSPACE.NOT_FOUND", "Workspace was not found.") : null;
    internal static SemanticRunResponse ToResponse(SemanticExtractionRun run) => new(run.Id, run.DocumentVersionId, run.ExtractorIdentity, run.ExtractorVersion, run.Status, run.StartedOnUtc, run.CompletedOnUtc, run.EntityCount, run.ClaimCount, run.RelationshipCount, run.ErrorCategory);
}

public sealed class GetSemanticEntitiesHandler : IQueryHandler<GetSemanticEntitiesQuery, IReadOnlyList<SemanticEntityResponse>>
{
    private readonly ICurrentUser _currentUser; private readonly IWorkspaceRepository _workspaces; private readonly ISemanticExtractionRepository _repository;
    public GetSemanticEntitiesHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticExtractionRepository repository) { _currentUser = currentUser; _workspaces = workspaces; _repository = repository; }
    public async Task<Result<IReadOnlyList<SemanticEntityResponse>>> Handle(GetSemanticEntitiesQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated) return Result<IReadOnlyList<SemanticEntityResponse>>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaces.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null) return Result<IReadOnlyList<SemanticEntityResponse>>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));
        var entities = await _repository.GetEntitiesAsync(request.WorkspaceId, cancellationToken);
        return Result<IReadOnlyList<SemanticEntityResponse>>.Success(entities.Select(entity => new SemanticEntityResponse(entity.Id, entity.WorkspaceId, entity.EntityType, entity.CanonicalName, entity.Aliases)).ToList());
    }
}

public sealed class GetSemanticClaimsHandler : IQueryHandler<GetSemanticClaimsQuery, IReadOnlyList<SemanticClaimResponse>>
{
    private readonly ICurrentUser _currentUser; private readonly IWorkspaceRepository _workspaces; private readonly ISemanticExtractionRepository _repository;
    public GetSemanticClaimsHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticExtractionRepository repository) { _currentUser = currentUser; _workspaces = workspaces; _repository = repository; }
    public async Task<Result<IReadOnlyList<SemanticClaimResponse>>> Handle(GetSemanticClaimsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated) return Result<IReadOnlyList<SemanticClaimResponse>>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaces.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null) return Result<IReadOnlyList<SemanticClaimResponse>>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));
        if (request.VersionId is not null && !await _repository.VersionBelongsToWorkspaceAsync(request.WorkspaceId, request.VersionId.Value, cancellationToken)) return Result<IReadOnlyList<SemanticClaimResponse>>.Failure(new Error("DOCUMENT_VERSION.NOT_FOUND", "Document version was not found."));
        var claims = await _repository.GetClaimsAsync(request.WorkspaceId, request.VersionId, request.EntityId, request.Predicate, request.AsOfUtc, request.ValidAtUtc, cancellationToken);
        return Result<IReadOnlyList<SemanticClaimResponse>>.Success(claims.Select(ToResponse).ToList());
    }
    internal static SemanticClaimResponse ToResponse(SemanticClaim claim) => new(claim.Id, claim.DocumentVersionId, claim.SubjectEntityId, claim.ObjectEntityId, claim.Subject, claim.Predicate, claim.ObjectValue, claim.ExtractionConfidence, claim.SourceAuthorityScore, claim.Status, claim.EvidenceId, claim.ValidFromUtc, claim.ValidToUtc, claim.AsOfUtc, claim.SourcePublishedOnUtc, claim.ValueKind, claim.NumericValue, claim.CurrencyCode, claim.Unit, claim.PeriodStartUtc, claim.PeriodEndUtc);
}

public sealed class GetSemanticEntityTypesHandler : IQueryHandler<GetSemanticEntityTypesQuery, IReadOnlyList<SemanticEntityTypeResponse>>
{
    private readonly ICurrentUser _currentUser; private readonly IWorkspaceRepository _workspaces; private readonly IBankingSemanticVocabulary _vocabulary;
    public GetSemanticEntityTypesHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, IBankingSemanticVocabulary vocabulary) { _currentUser = currentUser; _workspaces = workspaces; _vocabulary = vocabulary; }
    public async Task<Result<IReadOnlyList<SemanticEntityTypeResponse>>> Handle(GetSemanticEntityTypesQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated) return Result<IReadOnlyList<SemanticEntityTypeResponse>>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaces.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null) return Result<IReadOnlyList<SemanticEntityTypeResponse>>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));
        var types = await _vocabulary.GetEntityTypesAsync(request.WorkspaceId, cancellationToken);
        return Result<IReadOnlyList<SemanticEntityTypeResponse>>.Success(types.Select(item => new SemanticEntityTypeResponse(item.Id, item.Code, item.DisplayName, item.Description)).ToList());
    }
}

public sealed class GetSemanticPredicatesHandler : IQueryHandler<GetSemanticPredicatesQuery, IReadOnlyList<SemanticPredicateResponse>>
{
    private readonly ICurrentUser _currentUser; private readonly IWorkspaceRepository _workspaces; private readonly IBankingSemanticVocabulary _vocabulary;
    public GetSemanticPredicatesHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, IBankingSemanticVocabulary vocabulary) { _currentUser = currentUser; _workspaces = workspaces; _vocabulary = vocabulary; }
    public async Task<Result<IReadOnlyList<SemanticPredicateResponse>>> Handle(GetSemanticPredicatesQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated) return Result<IReadOnlyList<SemanticPredicateResponse>>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaces.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null) return Result<IReadOnlyList<SemanticPredicateResponse>>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));
        var predicates = await _vocabulary.GetPredicatesAsync(request.WorkspaceId, cancellationToken);
        return Result<IReadOnlyList<SemanticPredicateResponse>>.Success(predicates.Select(item => new SemanticPredicateResponse(item.Id, item.Code, item.DisplayName, item.AllowsEntityObject, item.AllowedSubjectTypes)).ToList());
    }
}

public sealed class GetSemanticClaimHandler : IQueryHandler<GetSemanticClaimQuery, SemanticClaimDetailsResponse>
{
    private readonly ICurrentUser _currentUser; private readonly IWorkspaceRepository _workspaces; private readonly ISemanticExtractionRepository _repository;
    public GetSemanticClaimHandler(ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticExtractionRepository repository) { _currentUser = currentUser; _workspaces = workspaces; _repository = repository; }
    public async Task<Result<SemanticClaimDetailsResponse>> Handle(GetSemanticClaimQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated) return Result<SemanticClaimDetailsResponse>.Failure(new Error("AUTH.UNAUTHORIZED", "Authentication is required."));
        if (await _workspaces.GetByIdAsync(_currentUser.UserId, request.WorkspaceId, cancellationToken) is null) return Result<SemanticClaimDetailsResponse>.Failure(new Error("WORKSPACE.NOT_FOUND", "Workspace was not found."));
        var details = await _repository.GetClaimAsync(request.WorkspaceId, request.ClaimId, cancellationToken);
        if (details is null) return Result<SemanticClaimDetailsResponse>.Failure(new Error("SEMANTIC_CLAIM.NOT_FOUND", "Semantic claim was not found."));
        return Result<SemanticClaimDetailsResponse>.Success(new SemanticClaimDetailsResponse(
            GetSemanticClaimsHandler.ToResponse(details.Claim),
            new SemanticEvidenceResponse(details.Evidence.Id, details.Evidence.DocumentVersionId, details.Evidence.DocumentSectionId, details.Evidence.SourceLocation, details.Evidence.PageNumber, details.Evidence.SheetName, details.Evidence.SlideNumber, details.Evidence.RowNumber, details.Evidence.ColumnNumber, details.Evidence.CellReference, details.Evidence.ExtractedText, details.Evidence.ExtractedValue),
            ToEntity(details.SubjectEntity), ToEntity(details.ObjectEntity)));
    }
    private static SemanticEntityResponse? ToEntity(SemanticEntity? entity) => entity is null ? null : new(entity.Id, entity.WorkspaceId, entity.EntityType, entity.CanonicalName, entity.Aliases);
}
