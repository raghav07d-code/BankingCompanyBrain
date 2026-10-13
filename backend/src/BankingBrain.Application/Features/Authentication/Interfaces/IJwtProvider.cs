using BankingBrain.Domain.Entities;

namespace BankingBrain.Application.Features.Authentication.Interfaces;

public interface IJwtProvider
{
    string GenerateAccessToken(User user);

    string GenerateRefreshToken();
}