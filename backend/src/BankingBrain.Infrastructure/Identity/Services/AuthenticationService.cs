using BankingBrain.Application.Common.Results;
using BankingBrain.Application.Features.Authentication.Commands.Login;
using BankingBrain.Application.Features.Authentication.Interfaces;

namespace BankingBrain.Infrastructure.Identity.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtProvider _jwtProvider;

    public AuthenticationService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtProvider jwtProvider)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtProvider = jwtProvider;
    }

    public async Task<Result<LoginResponse>> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(
            email,
            cancellationToken);

        if (user is null)
        {
            return Result<LoginResponse>.Failure(
                new Error(
                    "AUTH.INVALID_CREDENTIALS",
                    "Invalid email or password."));
        }

        if (!user.IsActive)
        {
            return Result<LoginResponse>.Failure(
                new Error(
                    "AUTH.USER_DISABLED",
                    "User account is disabled."));
        }

        var verified = _passwordHasher.Verify(
            password,
            user.PasswordHash);

        if (!verified)
        {
            return Result<LoginResponse>.Failure(
                new Error(
                    "AUTH.INVALID_CREDENTIALS",
                    "Invalid email or password."));
        }

        var accessToken = _jwtProvider.GenerateAccessToken(user);

        var refreshToken = _jwtProvider.GenerateRefreshToken();

        return Result<LoginResponse>.Success(
            new LoginResponse(
                user.Id,
                user.FullName,
                user.Email,
                accessToken,
                refreshToken));
    }
}