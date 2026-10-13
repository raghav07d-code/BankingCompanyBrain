using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Queries.GetKnowledgeSource;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class GetKnowledgeSourceEndpoint
{
    public static IEndpointRouteBuilder MapGetKnowledgeSource(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetKnowledgeSourceQuery(
                            workspaceId,
                            knowledgeSourceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Knowledge Sources")
            .WithName("GetKnowledgeSource");

        return endpoints;
    }
}