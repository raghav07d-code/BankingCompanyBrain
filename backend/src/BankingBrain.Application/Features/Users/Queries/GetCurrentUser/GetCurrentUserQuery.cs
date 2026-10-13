using BankingBrain.Application.Common.Abstractions;
using BankingBrain.Application.Common.Results;

namespace BankingBrain.Application.Features.Users.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery
    : IQuery<CurrentUserResponse>;