using BankingBrain.Application.Common.Results;
using MediatR;

namespace BankingBrain.Application.Common.Abstractions;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}