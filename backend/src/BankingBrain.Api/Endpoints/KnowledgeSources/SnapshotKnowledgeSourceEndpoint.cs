using BankingBrain.Api.Extensions;
using BankingBrain.Application.Common.Connectors.SnapshotKnowledgeSource;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class SnapshotKnowledgeSourceEndpoint
{
    public static IEndpointRouteBuilder MapSnapshotKnowledgeSource(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources/{knowledgeSourceId:guid}/snapshot",
                async (
                    Guid workspaceId,
                    Guid knowledgeSourceId,
                    SnapshotKnowledgeSourceRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new SnapshotKnowledgeSourceCommand(
                            workspaceId,
                            knowledgeSourceId,
                            request.Revision),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Knowledge Sources")
            .WithName("SnapshotKnowledgeSource");

        return endpoints;
    }

    public sealed record SnapshotKnowledgeSourceRequest(
        string? Revision);
}
