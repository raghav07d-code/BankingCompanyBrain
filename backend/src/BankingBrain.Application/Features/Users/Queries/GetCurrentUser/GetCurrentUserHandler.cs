using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Results;

namespace BankingBrain.Application.Features.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserHandler
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserHandler(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<Result<CurrentUserResponse>> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Task.FromResult(
                Result<CurrentUserResponse>.Failure(
                    new Error(
                        "AUTH.UNAUTHORIZED",
                        "Authentication is required.")));
        }

        var response = new CurrentUserResponse(
            _currentUser.UserId,
            _currentUser.Email,
            _currentUser.FullName);

        return Task.FromResult(
            Result<CurrentUserResponse>.Success(response));
    }
}