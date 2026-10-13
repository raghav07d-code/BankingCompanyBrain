using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Commands.ExtractSqlLineage;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class ExtractSqlLineageEndpoint
{
    public static IEndpointRouteBuilder MapExtractSqlLineage(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/sql-lineage/extract",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ExtractSqlLineageCommand(
                            workspaceId,
                            knowledgeSourceId),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("SqlLineage")
            .WithName("ExtractSqlLineage");

        return endpoints;
    }
}
