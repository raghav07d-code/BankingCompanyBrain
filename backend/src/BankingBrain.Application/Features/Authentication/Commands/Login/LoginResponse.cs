namespace BankingBrain.Application.Features.Authentication.Commands.Login;

public sealed record LoginResponse(
    Guid UserId,
    string FullName,
    string Email,
    string AccessToken,
    string RefreshToken);