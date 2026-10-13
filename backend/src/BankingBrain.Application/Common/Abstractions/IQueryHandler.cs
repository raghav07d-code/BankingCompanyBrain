using BankingBrain.Application.Common.Results;
using MediatR;

namespace BankingBrain.Application.Common.Abstractions;

public interface IQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}