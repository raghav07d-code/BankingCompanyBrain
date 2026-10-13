namespace BankingBrain.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid UserId { get; }

    string Email { get; }

    string FullName { get; }

    bool IsAuthenticated { get; }
}