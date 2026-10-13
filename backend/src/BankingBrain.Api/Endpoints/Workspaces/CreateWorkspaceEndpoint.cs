using BankingBrain.Application.Features.Workspaces.Commands.CreateWorkspace;
using BankingBrain.Api.Extensions;
using MediatR;

namespace BankingBrain.Api.Endpoints.Workspaces;

public static class CreateWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapCreateWorkspace(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/workspaces",
                async (
                    CreateWorkspaceCommand command,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        command,
                        cancellationToken);

                    if (result.IsSuccess)
                    {
                        return Results.Created(
                            $"/api/v1/workspaces/{result.Value.WorkspaceId}",
                            result.Value);
                    }

                    return result.ToHttpError();
                })
            .RequireAuthorization()
            .WithTags("Workspaces");

        return endpoints;
    }
}