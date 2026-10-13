using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractCodeIntelligence;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class ExtractCodeIntelligenceEndpoint
{
    public static IEndpointRouteBuilder MapExtractCodeIntelligence(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/code-intelligence/extract",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ExtractCodeIntelligenceCommand(
                            workspaceId,
                            knowledgeSourceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("CodeIntelligence")
            .WithName("ExtractCodeIntelligence");

        return endpoints;
    }
}
