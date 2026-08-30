using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Infrastructure.Security;
using MoneyRecord.Infrastructure.Services;
using MongoDB.Driver;

namespace MoneyRecord.Infrastructure;

/// <summary>
/// Infrastructure layer composition root (ARCH-006 §9) — MongoDB-backed.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MoneyRecord")
            ?? throw new InvalidOperationException("Connection string 'MoneyRecord' is not configured.");

        var databaseName = configuration.GetValue<string>("MongoDb:DatabaseName") ?? "moneyrecord";

        // Register MongoDB client directly for index creation and atomic operations
        var mongoClient = new MongoClient(connectionString);
        services.AddSingleton<IMongoClient>(mongoClient);
        services.AddSingleton(sp => mongoClient.GetDatabase(databaseName));

        services.AddDbContext<MoneyRecordDbContext>(options =>
            options.UseMongoDB(connectionString, databaseName));

        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IMoneyRecordDbContext,
            MoneyRecordDbContextAdapter>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IAuditLogger, AuditLogger>();
        services.AddScoped<MoneyRecord.Application.Customers.Common.ICustomerTransactionStats,
            MoneyRecord.Infrastructure.Persistence.CustomerTransactionStatsService>();

        // MongoDB atomic balance operations (replaces PG FOR UPDATE locks)
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IBalanceLocker,
            MongoBalanceLocker>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.ITxnNumberGenerator,
            MongoTxnNumberGenerator>();
        services.AddSingleton<IdempotencyKeyLockRegistry>();
        services.AddScoped<MoneyRecord.Application.Common.Interfaces.IIdempotencyStore,
            MongoIdempotencyStore>();
        // M7: cache-vs-ledger reconciliation + BalanceAfter chain verification.
        services.AddScoped<ReconciliationService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= 32,
                "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        services.AddOptions<MoneyRecord.Application.Auth.AuthTokenOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.RefreshTokenDays is >= 1 and <= 365,
                "Jwt:RefreshTokenDays must be between 1 and 365.")
            .ValidateOnStart();

        return services;
    }
}
