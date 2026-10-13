using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Commands.CreateDocument;
using MediatR;

namespace BankingBrain.Api.Endpoints.Documents;

public static class CreateDocumentEndpoint
{
    public static IEndpointRouteBuilder MapCreateDocument(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/documents",
                async (
                    Guid workspaceId,
                    CreateDocumentRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new CreateDocumentCommand(
                            workspaceId,
                            request.KnowledgeSourceId,
                            request.Name,
                            request.ExternalIdentifier,
                            request.MimeType),
                        cancellationToken);

                    if (result.IsSuccess)
                        return Results.Created(
                            $"/api/v1/workspaces/{workspaceId}/documents/{result.Value.DocumentId}",
                            result.Value);

                    return result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .WithName("CreateDocument");

        return endpoints;
    }

    public sealed record CreateDocumentRequest(
        Guid KnowledgeSourceId,
        string Name,
        string? ExternalIdentifier,
        string MimeType);
}