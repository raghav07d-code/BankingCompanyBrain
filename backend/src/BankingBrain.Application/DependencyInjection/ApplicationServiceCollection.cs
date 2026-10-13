using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using BankingBrain.Application.Behaviors;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using MediatR;

namespace BankingBrain.Application.DependencyInjection;

public static class ApplicationServiceCollection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(LoggingBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(PerformanceBehavior<,>));

        services.AddSingleton<ICodeRepositorySemanticMapper, CodeRepositorySemanticMapper>();

        return services;
    }
}