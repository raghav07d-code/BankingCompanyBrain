using BankingBrain.Application.Common.Abstractions;

namespace BankingBrain.Application.Features.Authentication.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password)
    : ICommand<LoginResponse>;