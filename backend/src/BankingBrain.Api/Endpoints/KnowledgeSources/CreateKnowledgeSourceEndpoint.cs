using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.KnowledgeSources.Commands.CreateKnowledgeSource;
using MediatR;

namespace BankingBrain.Api.Endpoints.KnowledgeSources;

public static class CreateKnowledgeSourceEndpoint
{
    public static IEndpointRouteBuilder MapCreateKnowledgeSource(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/knowledge-sources",
                async (
                    Guid workspaceId,
                    CreateKnowledgeSourceRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new CreateKnowledgeSourceCommand(
                            workspaceId,
                            request.Name,
                            request.Description,
                            request.SourceType,
                            request.ConnectorKey,
                            request.ExternalIdentifier),
                        cancellationToken);

                    if (result.IsSuccess)
                    {
                        return Results.Created(
                            $"/api/v1/workspaces/{workspaceId}/knowledge-sources/{result.Value!.KnowledgeSourceId}",
                            result.Value);
                    }

                    return result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Knowledge Sources")
            .WithName("CreateKnowledgeSource");

        return endpoints;
    }

    public sealed record CreateKnowledgeSourceRequest(
        string Name,
        string? Description,
        BankingBrain.Domain.Enums.KnowledgeSourceType SourceType,
        string? ConnectorKey,
        string? ExternalIdentifier);
}
