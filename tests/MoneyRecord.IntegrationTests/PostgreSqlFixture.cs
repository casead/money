using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using MoneyRecord.Application;
using MoneyRecord.Infrastructure;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// Boots the REAL Application+Infrastructure stacks against a dedicated PostgreSQL
/// test database (migrations applied from scratch per run â€” PLAN Â§3.2).
/// Admin connection: env MONEYRECORD_IT_ADMIN, else the dev Supabase instance.
/// A temporary database is created per run and dropped afterwards.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("MONEYRECORD_IT_ADMIN")
        ?? "Host=aws-0-ap-northeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.aoipgqgjbaaoktjrlncq;Password=erBK8sA0s8WxfF5T;SSL Mode=Require;Trust Server Certificate=true";

    public string DbName { get; } = $"mr_it_{Guid.NewGuid():N}"[..17];

    public string ConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString)
    {
        Database = DbName
    }.ConnectionString;

    private ServiceProvider? _provider;

    public ServiceProvider Services => _provider!;

    public async Task InitializeAsync()
    {
        // ---- create scratch database ----
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"""CREATE DATABASE "{DbName}" """;
            await cmd.ExecuteNonQueryAsync();
        }

        // ---- build the real DI stack against it ----
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MoneyRecord"] = ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config);

        // Test doubles for ambient context (real DB, real locks, real pipeline).
        // Default context = ShopAdmin of shop 1 (M11: ShopId must be non-null).
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(
            Domain.Common.Rbac.RolePermissionRegistry.AdminRoleId));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());

        _provider = services.BuildServiceProvider();

        // Apply migrations to a brand-new database (DBD schema from scratch every run).
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneyRecordDbContext>();
        await db.Database.MigrateAsync();

        // Seed actor users referenced by CreatedByUserId FKs.
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        // Shop id=1 comes from the LedgerAppendOnlyGuards migration seed.
        db.Users.Add(User.Create("it-admin", "stub::pw", "IT Admin",
            roleId: Domain.Common.Rbac.RolePermissionRegistry.AdminRoleId,
            actorUserId: 0, clock, shopId: 1));
        db.Users.Add(User.Create("it-staff", "stub::pw", "IT Staff",
            roleId: Domain.Common.Rbac.RolePermissionRegistry.StaffRoleId,
            actorUserId: 0, clock, shopId: 1));
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a fresh DI scope (one per logical request â€” mirrors HTTP).</summary>
    public IServiceScope CreateScope() => _provider!.CreateScope();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            await using (var term = conn.CreateCommand())
            {
                term.CommandText =
                    $"""SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DbName}' AND pid <> pg_backend_pid()""";
                await term.ExecuteNonQueryAsync();
            }
            await using var drop = conn.CreateCommand();
            drop.CommandText = $"""DROP DATABASE IF EXISTS "{DbName}" """;
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // best-effort cleanup; leftover IT databases are harmless and cheap on Supabase free tier
        }
    }
}

public sealed record TestCurrentUser : ICurrentUser
{
    private readonly long _userId;

    public TestCurrentUser(int roleId, long? shopId = 1)
    {
        RoleId = roleId;
        ShopId = roleId == Domain.Common.Rbac.RolePermissionRegistry.SuperAdminRoleId
            ? null : shopId;
        _userId = roleId switch
        {
            Domain.Common.Rbac.RolePermissionRegistry.SuperAdminRoleId => 1L,
            Domain.Common.Rbac.RolePermissionRegistry.AdminRoleId => 2L,
            _ => 3L
        };
    }

    public long? UserId => _userId;
    public string? UserName => "it";
    public int? RoleId { get; }
    public long? ShopId { get; }
}

public sealed class TestRequestContext : IRequestContext
{
    public string? IpAddress => "127.0.0.1";
    public string? DeviceInfo => "integration-test";
}

[CollectionDefinition("sql")]
public class SqlDatabaseCollection : ICollectionFixture<PostgreSqlFixture> { }

