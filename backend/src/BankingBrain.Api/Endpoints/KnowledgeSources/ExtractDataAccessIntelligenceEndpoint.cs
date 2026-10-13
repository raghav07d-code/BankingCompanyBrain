using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractDataAccessIntelligence;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class ExtractDataAccessIntelligenceEndpoint
{
    public static IEndpointRouteBuilder MapExtractDataAccessIntelligence(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/data-access-intelligence/extract",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ExtractDataAccessIntelligenceCommand(
                            workspaceId,
                            knowledgeSourceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("DataAccessIntelligence")
            .WithName("ExtractDataAccessIntelligence");

        return endpoints;
    }
}
