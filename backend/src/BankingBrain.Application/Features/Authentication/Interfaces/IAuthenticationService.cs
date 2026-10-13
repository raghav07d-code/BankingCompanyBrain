using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Authentication.Commands.Login;

namespace BankingBrain.Application.Features.Authentication.Interfaces;

public interface IAuthenticationService
{
    Task<Result<LoginResponse>> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken);
}