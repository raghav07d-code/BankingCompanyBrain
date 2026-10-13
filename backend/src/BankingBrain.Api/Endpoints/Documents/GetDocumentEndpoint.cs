using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Queries.GetDocument;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class GetDocumentEndpoint
{
    public static IEndpointRouteBuilder MapGetDocument(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetDocumentQuery(workspaceId, documentId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("GetDocument");

        return endpoints;
    }
}