using BankingBrain.Application.Features.Workspaces.Queries.GetWorkspace;
using BankingBrain.Api.Extensions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Workspaces;

public static class GetWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapGetWorkspace(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces/{workspaceId:guid}",
                async (
                    Guid workspaceId,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetWorkspaceQuery(workspaceId),
                        cancellationToken);

                    if (result.IsSuccess)
                        return Results.Ok(result.Value);

                    return result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Workspaces");

        return endpoints;
    }
}