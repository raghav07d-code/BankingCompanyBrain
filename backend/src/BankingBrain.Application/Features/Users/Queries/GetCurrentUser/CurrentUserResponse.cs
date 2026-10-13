namespace BankingBrain.Application.Features.Users.Queries.GetCurrentUser;

public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string FullName);