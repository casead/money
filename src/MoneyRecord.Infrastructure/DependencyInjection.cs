using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Infrastructure.Security;
using MoneyRecord.Infrastructure.Services;

namespace MoneyRecord.Infrastructure;

/// <summary>
/// Infrastructure layer composition root (ARCH-006 §9).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MoneyRecord")
            ?? throw new InvalidOperationException("Connection string 'MoneyRecord' is not configured.");

        services.AddDbContext<MoneyRecordDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                npgsql.CommandTimeout(30);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "migration");
            }));

        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IMoneyRecordDbContext,
            MoneyRecordDbContextAdapter>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IAuditLogger, AuditLogger>();
        services.AddScoped<MoneyRecord.Application.Customers.Common.ICustomerTransactionStats,
            MoneyRecord.Infrastructure.Persistence.CustomerTransactionStatsService>();
        // UPDLOCK balance-row locking (BR-035) — scoped with DbContext.
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IBalanceLocker,
            UpdlockBalanceLocker>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.ITxnNumberGenerator,
            TxnNumberGenerator>();
        services.AddSingleton<IdempotencyKeyLockRegistry>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IIdempotencyStore,
            IdempotencyStore>();
        // M7: cache-vs-ledger reconciliation + BalanceAfter chain verification.
        services.AddScoped<ReconciliationService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= 32,
                "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        // Application-layer token lifetimes share the same "Jwt" section (A3 fix:
        // refresh-token lifetime was previously hardcoded to 7 days in handlers).
        services.AddOptions<MoneyRecord.Application.Auth.AuthTokenOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.RefreshTokenDays is >= 1 and <= 365,
                "Jwt:RefreshTokenDays must be between 1 and 365.")
            .ValidateOnStart();

        return services;
    }
}
