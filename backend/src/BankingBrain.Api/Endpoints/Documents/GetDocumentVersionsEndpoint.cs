using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Queries.GetDocumentVersions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class GetDocumentVersionsEndpoint
{
    public static IEndpointRouteBuilder MapGetDocumentVersions(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/versions",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetDocumentVersionsQuery(workspaceId, documentId),
                        cancellationToken);
                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("GetDocumentVersions");

        return endpoints;
    }
}