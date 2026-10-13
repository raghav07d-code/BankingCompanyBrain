using BankingBrain.Application.Features.Users.Queries.GetCurrentUser;
using BankingBrain.Api.Extensions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Users;

public static class GetCurrentUserEndpoint
{
    public static IEndpointRouteBuilder MapGetCurrentUser(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/users/me",
                async (
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetCurrentUserQuery(),
                        cancellationToken);

                    return result.IsSuccess
                        ? Results.Ok(result.Value)
                        : result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Users");

        return endpoints;
    }
}