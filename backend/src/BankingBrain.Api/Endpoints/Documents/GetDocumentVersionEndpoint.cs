using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Queries.GetDocumentVersion;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class GetDocumentVersionEndpoint
{
    public static IEndpointRouteBuilder MapGetDocumentVersion(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions/{versionId:guid}",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    Guid versionId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetDocumentVersionQuery(workspaceId, documentId, versionId),
                        cancellationToken);
                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("GetDocumentVersion");

        return endpoints;
    }
}