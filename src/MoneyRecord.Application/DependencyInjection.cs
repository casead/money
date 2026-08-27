using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Common.Behaviors;

namespace MoneyRecord.Application;

/// <summary>
/// Application layer composition root (ARCH-006 §7).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddScoped<MoneyRecord.Application.Fees.Services.IFeeCalculator,
            MoneyRecord.Application.Fees.Services.FeeCalculator>();

        return services;
    }
}
