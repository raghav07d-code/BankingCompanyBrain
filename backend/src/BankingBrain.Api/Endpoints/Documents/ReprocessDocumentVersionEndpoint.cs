using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Commands.ReprocessDocumentVersion;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class ReprocessDocumentVersionEndpoint
{
    public static IEndpointRouteBuilder MapReprocessDocumentVersion(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/reprocess",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    Guid versionId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ReprocessDocumentVersionCommand(
                            workspaceId,
                            documentId,
                            versionId),
                        cancellationToken);
                    return result.IsSuccess
                        ? Results.Accepted($"/api/v1/workspaces/{workspaceId}/documents/{documentId}/versions/{versionId}", result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("ReprocessDocumentVersion");

        return endpoints;
    }
}