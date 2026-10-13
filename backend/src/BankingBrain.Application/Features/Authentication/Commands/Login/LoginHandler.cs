using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Authentication.Interfaces;

namespace BankingBrain.Application.Features.Authentication.Commands.Login;

public sealed class LoginHandler
    : ICommandHandler<LoginCommand, LoginResponse>
{
    private readonly IAuthenticationService _authenticationService;

    public LoginHandler(
        IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public async Task<Result<LoginResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        return await _authenticationService.LoginAsync(
            request.Email,
            request.Password,
            cancellationToken);
    }
}