using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractApiIntelligence;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class ExtractApiIntelligenceEndpoint
{
    public static IEndpointRouteBuilder MapExtractApiIntelligence(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/api-intelligence/extract",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ExtractApiIntelligenceCommand(
                            workspaceId,
                            knowledgeSourceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("ApiIntelligence")
            .WithName("ExtractApiIntelligence");

        return endpoints;
    }
}
