using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.KnowledgeSources.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public sealed record KnowledgeSourceRefreshRunResponse(
    Guid RunId,
    Guid WorkspaceId,
    Guid KnowledgeSourceId,
    string ExtractorIdentity,
    string Revision,
    string Status,
    DateTime QueuedOnUtc,
    DateTime? StartedOnUtc,
    DateTime? CompletedOnUtc,
    string? ErrorCategory,
    string? Error);

/// <summary>
/// Exposes the explicit, observable state of the latest queued refresh for a (workspace,
/// knowledge source, extractor) tuple - callers (including evaluation tooling) use this to
/// wait for a *specific* snapshot's extraction to reach a terminal state, instead of
/// inferring "the pipeline settled" from a global extraction-run count that says nothing
/// about which snapshot it was for.
/// </summary>
public static class GetKnowledgeSourceRefreshRunEndpoint
{
    public static IEndpointRouteBuilder MapGetKnowledgeSourceRefreshRun(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/refresh-runs/latest",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    string extractorIdentity,
                    ICurrentUser currentUser,
                    IWorkspaceRepository workspaces,
                    IKnowledgeSourceRefreshRunRepository refreshRuns,
                    CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated) return Results.Unauthorized();
                    if (await workspaces.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null) return Results.NotFound();

                    var run = await refreshRuns.GetLatestAsync(workspaceId, knowledgeSourceId, extractorIdentity, cancellationToken);

                    return run is null
                        ? Results.NotFound()
                        : Results.Ok(new KnowledgeSourceRefreshRunResponse(
                            run.Id,
                            run.WorkspaceId,
                            run.KnowledgeSourceId,
                            run.ExtractorIdentity,
                            run.Revision,
                            run.Status.ToString(),
                            run.QueuedOnUtc,
                            run.StartedOnUtc,
                            run.CompletedOnUtc,
                            run.ErrorCategory,
                            run.Error));
                })
            .RequireAuthorization()
            .WithTags("KnowledgeSources")
            .WithName("GetLatestKnowledgeSourceRefreshRun");

        return endpoints;
    }
}
