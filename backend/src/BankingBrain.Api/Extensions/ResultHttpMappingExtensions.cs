using BankingBrain.Application.Common.Results;

namespace BankingBrain.Api.Extensions;

public static class ResultHttpMappingExtensions
{
    public static IResult ToHttpError(this Result result)
    {
        var statusCode = result.Error.Code switch
        {
            "AUTH.UNAUTHORIZED" => StatusCodes.Status401Unauthorized,
            "WORKSPACE.NOT_FOUND" => StatusCodes.Status404NotFound,
            "KNOWLEDGE_SOURCE.NOT_FOUND" => StatusCodes.Status404NotFound,
            "DOCUMENT.NOT_FOUND" => StatusCodes.Status404NotFound,
            "DOCUMENT_VERSION.NOT_FOUND" => StatusCodes.Status404NotFound,
            "DOCUMENT.UNSUPPORTED_TYPE" => StatusCodes.Status400BadRequest,
            "DOCUMENT_VERSION.INVALID_STATE" => StatusCodes.Status400BadRequest,
            "DOCUMENT_VERSION.BUSY" => StatusCodes.Status409Conflict,
            "WORKSPACE.DUPLICATE" => StatusCodes.Status409Conflict,
            "KNOWLEDGE_SOURCE.DUPLICATE" => StatusCodes.Status409Conflict,
            "DOCUMENT.DUPLICATE" => StatusCodes.Status409Conflict,
            "DOCUMENT.CONFLICT" => StatusCodes.Status409Conflict,
            "SEMANTIC_CLAIM.NOT_FOUND" => StatusCodes.Status404NotFound,
            "SEMANTIC_EXTRACTION.NOT_READY" => StatusCodes.Status409Conflict,
            "KNOWLEDGE_SOURCE.NOT_GIT_BACKED" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(
            result.Error,
            statusCode: statusCode);
    }
}