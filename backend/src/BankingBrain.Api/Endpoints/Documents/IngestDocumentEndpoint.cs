using BankingBrain.Api.Extensions;
using BankingBrain.Application.Features.Documents.Commands.IngestDocument;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace BankingBrain.Api.Endpoints.Documents;

public static class IngestDocumentEndpoint
{
    public static IEndpointRouteBuilder MapIngestDocument(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces/{workspaceId:guid}/documents/{documentId:guid}/ingest",
                async (
                    Guid workspaceId,
                    Guid documentId,
                    HttpRequest httpRequest,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    if (!httpRequest.HasFormContentType)
                        return Results.BadRequest(new
                        {
                            Code = "DOCUMENT.MULTIPART_REQUIRED",
                            Description = "A multipart/form-data file upload is required."
                        });

                    var boundary = HeaderUtilities.RemoveQuotes(
                        MediaTypeHeaderValue.Parse(httpRequest.ContentType!).Boundary).Value;
                    if (string.IsNullOrWhiteSpace(boundary))
                        return Results.BadRequest(new
                        {
                            Code = "DOCUMENT.MULTIPART_INVALID",
                            Description = "The multipart boundary is missing."
                        });

                    var reader = new MultipartReader(boundary, httpRequest.Body);
                    MultipartSection? section;
                    while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
                    {
                        if (!ContentDispositionHeaderValue.TryParse(
                                section.ContentDisposition,
                                out var disposition) ||
                            !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(disposition.FileName.Value))
                            continue;

                        var fileName = Path.GetFileName(disposition.FileName.Value);
                        var mediaType = string.IsNullOrWhiteSpace(section.ContentType)
                            ? "application/octet-stream"
                            : section.ContentType;
                        var result = await sender.Send(
                            new IngestDocumentCommand(
                                workspaceId,
                                documentId,
                                fileName,
                                mediaType,
                                section.Body,
                                0),
                            cancellationToken);
                        if (!result.IsSuccess)
                            return result.ToHttpError();

                        return Results.Accepted(
                            $"/api/v1/workspaces/{workspaceId}/documents/{documentId}/versions/{result.Value.DocumentVersionId}",
                            result.Value);
                    }

                    return Results.BadRequest(new
                    {
                        Code = "DOCUMENT.FILE_REQUIRED",
                        Description = "A file part is required."
                    });
                })
            .RequireAuthorization()
            .WithTags("Documents")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<IngestDocumentResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .WithName("IngestDocument");

        return endpoints;
    }
}
