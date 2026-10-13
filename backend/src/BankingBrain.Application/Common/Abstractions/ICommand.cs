using BankingBrain.Application.Common.Results;
using MediatR;

namespace BankingBrain.Application.Common.Abstractions;

public interface ICommand : IRequest<Result>
{
}

public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}