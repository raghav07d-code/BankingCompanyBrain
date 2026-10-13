using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Queries.GetDocuments;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class GetDocumentsEndpoint
{
    public static IEndpointRouteBuilder MapGetDocuments(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/documents",
                async (
                    Guid workspaceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetDocumentsQuery(workspaceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("GetDocuments");

        return endpoints;
    }
}