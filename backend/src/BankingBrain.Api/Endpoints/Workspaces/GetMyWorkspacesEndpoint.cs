using BankingBrain.Application.Features.Workspaces.Queries.GetMyWorkspaces;
using BankingBrain.Api.Extensions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Workspaces;

public static class GetMyWorkspacesEndpoint
{
    public static IEndpointRouteBuilder MapGetMyWorkspaces(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/workspaces",
                async (
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetMyWorkspacesQuery(),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Workspaces");

        return endpoints;
    }
}
