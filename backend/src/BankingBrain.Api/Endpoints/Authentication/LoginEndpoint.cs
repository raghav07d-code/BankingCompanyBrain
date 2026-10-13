using BankingBrain.Application.Features.Authentication.Commands.Login;
using BankingBrain.Api.Extensions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Authentication;

public static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLoginEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/auth/login",
            async (
                LoginRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new LoginCommand(
                        request.Email,
                        request.Password),
                    cancellationToken);

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.ToHttpError();
            });

        return endpoints;
    }
}