using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Semantics.Commands.ExtractSemantics;
using BankingBrain.Application.Features.Semantics.Queries;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Application.Common.Embeddings;
using BankingBrain.Application.Common.Graph;
using BankingBrain.Application.Common.GroundedIntelligence;
using BankingBrain.Infrastructure.Persistence.Context;
using BankingBrain.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace BankingBrain.Api.Endpoints.Semantics;

public static class SemanticEndpoints
{
    public static IEndpointRouteBuilder MapSemanticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/semantic-extraction",
                async (Guid workspaceId, Guid documentId, Guid versionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new ExtractSemanticsCommand(workspaceId, documentId, versionId), cancellationToken);
                    return result.IsSuccess
                        ? Results.Accepted($"/api/v1/workspaces/{workspaceId}/documents/{documentId}/versions/{versionId}/semantic-extraction/runs/{result.Value.ExtractionRunId}", result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization().WithTags("Semantics").WithName("ExtractDocumentSemantics");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/semantic-extraction/runs",
                async (Guid workspaceId, Guid documentId, Guid versionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetSemanticRunsQuery(workspaceId, versionId), cancellationToken);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError();
                })
            .RequireAuthorization().WithTags("Semantics").WithName("GetSemanticExtractionRuns");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/entities",
                async (Guid workspaceId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetSemanticEntitiesQuery(workspaceId), cancellationToken);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError();
                })
            .RequireAuthorization().WithTags("Semantics").WithName("GetSemanticEntities");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/claims",
                async (Guid workspaceId, Guid? versionId, Guid? entityId, string? predicate, DateTime? asOfUtc, DateTime? validAtUtc, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetSemanticClaimsQuery(workspaceId, versionId, entityId, predicate, asOfUtc, validAtUtc), cancellationToken);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError();
                })
            .RequireAuthorization().WithTags("Semantics").WithName("GetSemanticClaims");

        endpoints.MapGet("/api/v1/workspaces/{workspaceId:guid}/semantic/entity-types", async (Guid workspaceId, ISender sender, CancellationToken cancellationToken) => { var result = await sender.Send(new GetSemanticEntityTypesQuery(workspaceId), cancellationToken); return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError(); }).RequireAuthorization().WithTags("Semantics").WithName("GetSemanticEntityTypes");
        endpoints.MapGet("/api/v1/workspaces/{workspaceId:guid}/semantic/predicates", async (Guid workspaceId, ISender sender, CancellationToken cancellationToken) => { var result = await sender.Send(new GetSemanticPredicatesQuery(workspaceId), cancellationToken); return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError(); }).RequireAuthorization().WithTags("Semantics").WithName("GetSemanticPredicates");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/claims/{claimId:guid}",
                async (Guid workspaceId, Guid claimId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetSemanticClaimQuery(workspaceId, claimId), cancellationToken);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpError();
                })
            .RequireAuthorization().WithTags("Semantics").WithName("GetSemanticClaim");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/entities",
                async (Guid workspaceId, string entityType, string name, string? externalIdentifier, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var entity = await queries.FindEntityAsync(workspaceId, entityType, name, externalIdentifier, cancellationToken);
                    return entity is null ? Results.NotFound() : Results.Ok(entity);
                })
            .RequireAuthorization().WithTags("SemanticQueries").WithName("FindSemanticEntity");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/claims",
                async (Guid workspaceId, string? subject, string? predicate, string? objectValue, DateTime? asOfUtc, DateTime? validAtUtc, DateTime? validFromUtc, DateTime? validToUtc, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var result = await queries.QueryClaimsAsync(workspaceId, new SemanticClaimQuery(subject, predicate, objectValue, asOfUtc, validAtUtc, validFromUtc, validToUtc), cancellationToken);
                    return Results.Ok(ToResponse(result));
                })
            .RequireAuthorization().WithTags("SemanticQueries").WithName("QuerySemanticClaims");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/claims/latest",
                async (Guid workspaceId, string? subject, string? predicate, string? objectValue, DateTime? asOfUtc, DateTime? validAtUtc, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var claim = await queries.GetLatestClaimAsync(workspaceId, new SemanticClaimQuery(subject, predicate, objectValue, asOfUtc, validAtUtc), cancellationToken);
                    return claim is null ? Results.NotFound() : Results.Ok(ToResponse(claim));
                })
            .RequireAuthorization().WithTags("SemanticQueries").WithName("GetLatestSemanticClaim");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/relationships",
                async (Guid workspaceId, Guid? subjectEntityId, Guid? objectEntityId, string? predicate, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var result = await queries.QueryRelationshipsAsync(workspaceId, new SemanticRelationshipQuery(subjectEntityId, objectEntityId, predicate), cancellationToken);
                    return Results.Ok(ToResponse(result));
                })
            .RequireAuthorization().WithTags("SemanticQueries").WithName("QuerySemanticRelationships");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/evidence/{claimId:guid}",
                async (Guid workspaceId, Guid claimId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var details = await queries.GetEvidenceAsync(workspaceId, claimId, cancellationToken);
                    return details is null ? Results.NotFound() : Results.Ok(new SemanticClaimDetailsResponse(ToResponse(details.Claim), ToResponse(details.Evidence), details.SubjectEntity is null ? null : ToResponse(details.SubjectEntity), details.ObjectEntity is null ? null : ToResponse(details.ObjectEntity)));
                })
            .RequireAuthorization().WithTags("SemanticQueries").WithName("GetSemanticQueryEvidence");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/entities",
                async (Guid workspaceId, string? entityType, string? canonicalName, string? externalIdentifier, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var entity = await queries.FindCodeEntityAsync(workspaceId, entityType, canonicalName, externalIdentifier, cancellationToken);
                    return entity is null ? Results.NotFound() : Results.Ok(entity);
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("FindCodeEntity");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/entities/{entityId:guid}/related",
                async (Guid workspaceId, Guid entityId, string predicate, SemanticRelationshipDirection direction, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var related = await queries.GetRelatedEntitiesAsync(workspaceId, entityId, predicate, direction, cancellationToken);
                    return Results.Ok(related);
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("GetRelatedCodeEntities");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/projects/{projectEntityId:guid}/types",
                async (Guid workspaceId, Guid projectEntityId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var types = await queries.GetTypesInProjectAsync(workspaceId, projectEntityId, cancellationToken);
                    return Results.Ok(types);
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("GetTypesInProject");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/types/{typeEntityId:guid}/members",
                async (Guid workspaceId, Guid typeEntityId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var members = await queries.GetTypeMembersAsync(workspaceId, typeEntityId, cancellationToken);
                    return Results.Ok(members);
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("GetTypeMembers");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/entities/{entityId:guid}/provenance",
                async (Guid workspaceId, Guid entityId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var provenance = await queries.GetEntityProvenanceAsync(workspaceId, entityId, cancellationToken);
                    return Results.Ok(provenance.Select(ToResponse));
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("GetCodeEntityProvenance");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/code/relationships/{relationshipId:guid}/evidence",
                async (Guid workspaceId, Guid relationshipId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var details = await queries.GetRelationshipEvidenceAsync(workspaceId, relationshipId, cancellationToken);
                    return details is null ? Results.NotFound() : Results.Ok(ToResponse(details));
                })
            .RequireAuthorization().WithTags("CodeIntelligenceQueries").WithName("GetCodeRelationshipEvidence");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/sql/columns/{columnEntityId:guid}/dependents",
                async (Guid workspaceId, Guid columnEntityId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var dependents = await queries.GetSqlColumnDependentsAsync(workspaceId, columnEntityId, cancellationToken);
                    return Results.Ok(dependents);
                })
            .RequireAuthorization().WithTags("SqlLineageQueries").WithName("GetSqlColumnDependents");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/sql/entities/{entityId:guid}/impact",
                async (Guid workspaceId, Guid entityId, int? maxDepth, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var depth = Math.Clamp(maxDepth ?? 5, 1, 10);
                    var impact = await queries.GetSqlImpactAsync(workspaceId, entityId, depth, cancellationToken);
                    return Results.Ok(impact);
                })
            .RequireAuthorization().WithTags("SqlLineageQueries").WithName("GetSqlImpact");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/api/entities/{endpointEntityId:guid}/reachable",
                async (Guid workspaceId, Guid endpointEntityId, int? maxDepth, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var depth = Math.Clamp(maxDepth ?? 5, 1, 10);
                    var reachable = await queries.GetReachableCodePathAsync(workspaceId, endpointEntityId, depth, cancellationToken);
                    return Results.Ok(reachable);
                })
            .RequireAuthorization().WithTags("ApiIntelligenceQueries").WithName("GetReachableCodePath");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/query/api/entities/{methodEntityId:guid}/affected-endpoints",
                async (Guid workspaceId, Guid methodEntityId, int? maxDepth, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticQueryService queries, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var depth = Math.Clamp(maxDepth ?? 5, 1, 10);
                    var affected = await queries.GetEndpointsAffectedByMethodAsync(workspaceId, methodEntityId, depth, cancellationToken);
                    return Results.Ok(affected);
                })
            .RequireAuthorization().WithTags("ApiIntelligenceQueries").WithName("GetEndpointsAffectedByMethod");

        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/embeddings/index",
                async (Guid workspaceId, Guid versionId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ApplicationDbContext db, ISemanticEmbeddingService embeddings, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    if (!await db.DocumentVersions.AnyAsync(version => version.WorkspaceId == workspaceId && version.Id == versionId, cancellationToken)) return Results.NotFound();
                    var documents = new List<SemanticEmbeddingDocument>();
                    var sections = await db.DocumentSections.AsNoTracking().Where(section => section.DocumentVersionId == versionId).ToListAsync(cancellationToken);
                    documents.AddRange(sections.Where(section => !string.IsNullOrWhiteSpace(section.Text ?? section.Value)).Select(section => new SemanticEmbeddingDocument("DOCUMENT_SECTION", section.Id, versionId, section.Id, null, null, null, section.Text ?? section.Value ?? string.Empty, SemanticEmbeddingService.HashContent(section.Text ?? section.Value ?? string.Empty), new Dictionary<string, string> { ["source.location"] = section.SourceLocation ?? $"section:{section.Sequence}" })));
                    var claims = await db.SemanticClaims.AsNoTracking().Where(claim => claim.WorkspaceId == workspaceId && claim.DocumentVersionId == versionId).ToListAsync(cancellationToken);
                    documents.AddRange(claims.Select(claim => new SemanticEmbeddingDocument("SEMANTIC_CLAIM", claim.Id, versionId, null, claim.SubjectEntityId, claim.Id, claim.EvidenceId, $"{claim.Subject} {claim.Predicate} {claim.ObjectValue}", SemanticEmbeddingService.HashContent($"{claim.Subject}|{claim.Predicate}|{claim.ObjectValue}|{claim.AsOfUtc:O}|{claim.ValidFromUtc:O}|{claim.ValidToUtc:O}"), new Dictionary<string, string> { ["semantic.predicate"] = claim.Predicate })));
                    var entities = await db.SemanticEntities.AsNoTracking().Where(entity => entity.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
                    documents.AddRange(entities.Select(entity => new SemanticEmbeddingDocument("SEMANTIC_ENTITY", entity.Id, null, null, entity.Id, null, null, $"{entity.EntityType} {entity.CanonicalName} {entity.Aliases}", SemanticEmbeddingService.HashContent($"{entity.EntityType}|{entity.CanonicalName}|{entity.Aliases}"), new Dictionary<string, string> { ["semantic.entityType"] = entity.EntityType })));
                    await embeddings.IndexAsync(workspaceId, documents, cancellationToken);
                    return Results.Ok(new { Indexed = documents.Count });
                })
            .RequireAuthorization().WithTags("SemanticEmbeddings").WithName("IndexSemanticEmbeddings");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/embeddings/search",
                async (Guid workspaceId, string query, int? limit, Guid? documentVersionId, string? sourceType, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticEmbeddingService embeddings, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var results = await embeddings.SearchAsync(workspaceId, new SemanticEmbeddingSearchRequest(query, limit ?? 10, new SemanticEmbeddingFilter(documentVersionId, sourceType)), cancellationToken);
                    return Results.Ok(results);
                })
            .RequireAuthorization().WithTags("SemanticEmbeddings").WithName("SearchSemanticEmbeddings");

        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/graph/rebuild",
                async (Guid workspaceId, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticGraphService graph, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    await graph.RebuildAsync(workspaceId, cancellationToken);
                    return Results.Ok(new { Rebuilt = true });
                })
            .RequireAuthorization().WithTags("SemanticGraph").WithName("RebuildSemanticGraph");

        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/graph",
                async (Guid workspaceId, Guid? subjectEntityId, Guid? objectEntityId, string? predicate, DateTime? asOfUtc, DateTime? validAtUtc, int? maxDepth, ICurrentUser currentUser, IWorkspaceRepository workspaces, ISemanticGraphService graph, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    var result = await graph.QueryAsync(workspaceId, new SemanticGraphQuery(subjectEntityId, objectEntityId, predicate, asOfUtc, validAtUtc, maxDepth ?? 1), cancellationToken);
                    return Results.Ok(result);
                })
            .RequireAuthorization().WithTags("SemanticGraph").WithName("QuerySemanticGraph");

        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/semantic/intelligence/answer",
                async (Guid workspaceId, GroundedQuestion question, ICurrentUser currentUser, IWorkspaceRepository workspaces, IGroundedIntelligenceService intelligence, CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();
                    return Results.Ok(await intelligence.AnswerAsync(workspaceId, question, cancellationToken));
                })
            .RequireAuthorization().WithTags("GroundedIntelligence").WithName("AnswerGroundedSemanticQuestion");

        return endpoints;
    }

    private static SemanticQueryResponse ToResponse(SemanticQueryResult result) => new(
        result.Claims.Select(ToResponse).ToList(),
        result.Evidence.Select(evidence => new SemanticEvidenceResponse(evidence.Id, evidence.DocumentVersionId, evidence.DocumentSectionId, evidence.SourceLocation, evidence.PageNumber, evidence.SheetName, evidence.SlideNumber, evidence.RowNumber, evidence.ColumnNumber, evidence.CellReference, evidence.ExtractedText, evidence.ExtractedValue)).ToList(),
        result.Relationships);

    private static SemanticRelationshipDetailsResponse ToResponse(SemanticRelationshipDetails details) => new(
        new SemanticRelationshipResponse(details.Relationship.Id, details.Relationship.WorkspaceId, details.Relationship.DocumentVersionId, details.Relationship.SubjectEntityId, details.Relationship.ObjectEntityId, details.Relationship.RelationType, details.Relationship.ExtractionConfidence, details.Relationship.EvidenceId),
        new SemanticEvidenceResponse(details.Evidence.Id, details.Evidence.DocumentVersionId, details.Evidence.DocumentSectionId, details.Evidence.SourceLocation, details.Evidence.PageNumber, details.Evidence.SheetName, details.Evidence.SlideNumber, details.Evidence.RowNumber, details.Evidence.ColumnNumber, details.Evidence.CellReference, details.Evidence.ExtractedText, details.Evidence.ExtractedValue),
        details.SubjectEntity is null ? null : ToResponse(details.SubjectEntity),
        details.ObjectEntity is null ? null : ToResponse(details.ObjectEntity));

    private static SemanticClaimResponse ToResponse(Domain.Entities.Semantics.SemanticClaim claim) => new(claim.Id, claim.DocumentVersionId, claim.SubjectEntityId, claim.ObjectEntityId, claim.Subject, claim.Predicate, claim.ObjectValue, claim.ExtractionConfidence, claim.SourceAuthorityScore, claim.Status, claim.EvidenceId, claim.ValidFromUtc, claim.ValidToUtc, claim.AsOfUtc, claim.SourcePublishedOnUtc, claim.ValueKind, claim.NumericValue, claim.CurrencyCode, claim.Unit, claim.PeriodStartUtc, claim.PeriodEndUtc);
    private static SemanticEvidenceResponse ToResponse(Domain.Entities.Semantics.SemanticEvidence evidence) => new(evidence.Id, evidence.DocumentVersionId, evidence.DocumentSectionId, evidence.SourceLocation, evidence.PageNumber, evidence.SheetName, evidence.SlideNumber, evidence.RowNumber, evidence.ColumnNumber, evidence.CellReference, evidence.ExtractedText, evidence.ExtractedValue);
    private static SemanticEntityResponse ToResponse(Domain.Entities.Semantics.SemanticEntity entity) => new(entity.Id, entity.WorkspaceId, entity.EntityType, entity.CanonicalName, entity.Aliases);
}
