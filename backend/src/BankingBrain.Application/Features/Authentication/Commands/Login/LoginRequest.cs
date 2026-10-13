namespace BankingBrain.Application.Features.Authentication.Commands.Login;

public sealed record LoginRequest(
    string Email,
    string Password);