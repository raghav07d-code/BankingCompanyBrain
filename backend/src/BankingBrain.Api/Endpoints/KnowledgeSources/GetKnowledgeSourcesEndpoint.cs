using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSources;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class GetKnowledgeSourcesEndpoint
{
    public static IEndpointRouteBuilder MapGetKnowledgeSources(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources",
                async (
                    Guid workspaceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetKnowledgeSourcesQuery(workspaceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Knowledge Sources")
            .WithName("GetKnowledgeSources");

        return endpoints;
    }
}