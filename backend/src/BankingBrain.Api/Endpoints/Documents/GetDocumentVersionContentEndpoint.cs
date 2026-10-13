using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Features.Documents.Interfaces;
using BankingBrain.Application.Features.Workspaces.Interfaces;
using BankingBrain.Api.Extensions;

namespace BankingBrain.Api.Endpoints.Documents;

public static class GetDocumentVersionContentEndpoint
{
    public static IEndpointRouteBuilder MapGetDocumentVersionContent(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/content",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    Guid versionId,
                    ICurrentUser currentUser,
                    IWorkspaceRepository workspaceRepository,
                    IDocumentVersionRepository versionRepository,
                    IObjectStorage storage,
                    CancellationToken cancellationToken) =>
                {
                    if (!currentUser.IsAuthenticated)
                        return Results.Unauthorized();
                    if (await workspaceRepository.GetByIdAsync(currentUser.UserId, workspaceId, cancellationToken) is null)
                        return Results.NotFound();
                    var version = await versionRepository.GetByIdAsync(workspaceId, documentId, versionId, cancellationToken);
                    if (version is null)
                        return Results.NotFound();
                    try
                    {
                        var stream = await storage.OpenReadAsync(workspaceId.ToString(), version.StorageKey, cancellationToken);
                        return Results.File(stream, version.MediaType, version.FileName, enableRangeProcessing: true);
                    }
                    catch (FileNotFoundException)
                    {
                        return Results.NotFound();
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return Results.NotFound();
                    }
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("GetDocumentVersionContent");
        return endpoints;
    }
}
