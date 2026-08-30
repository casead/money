using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MoneyRecord.Application;
using MoneyRecord.Infrastructure;
using MoneyRecord.Infrastructure.Persistence;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// Boots the REAL Application+Infrastructure stacks against a dedicated MongoDB
/// test database (created from scratch per run).
/// Uses the MongoDB Atlas connection string from env MONEYRECORD_IT_MONGO or the dev cluster.
/// </summary>
public sealed class MongoDbFixture : IAsyncLifetime
{
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("MONEYRECORD_IT_MONGO")
        ?? "mongodb+srv://agent_note:Aung2003%40%24@cluster0.6k7hxvp.mongodb.net/?appName=Cluster0";

    public string DbName { get; } = $"mr_it_{Guid.NewGuid():N}"[..17];

    private ServiceProvider? _provider;
    private MongoClient? _client;

    public ServiceProvider Services => _provider!;
    public string ConnectionString { get; } = AdminConnectionString;

    public async Task InitializeAsync()
    {
        _client = new MongoClient(AdminConnectionString);

        // ---- build the real DI stack against it ----
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MoneyRecord"] = AdminConnectionString,
                ["MongoDb:DatabaseName"] = DbName
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config);

        // Test doubles for ambient context (real DB, real locks, real pipeline).
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(
            Domain.Common.Rbac.RolePermissionRegistry.AdminRoleId));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());

        _provider = services.BuildServiceProvider();

        // Ensure database schema via EnsureCreated (MongoDB creates collections on first write).
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MoneyRecordDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed actor users.
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        db.Users.Add(User.Create("it-admin", "stub::pw", "IT Admin",
            roleId: Domain.Common.Rbac.RolePermissionRegistry.AdminRoleId,
            actorUserId: 0, clock, shopId: 1));
        db.Users.Add(User.Create("it-staff", "stub::pw", "IT Staff",
            roleId: Domain.Common.Rbac.RolePermissionRegistry.StaffRoleId,
            actorUserId: 0, clock, shopId: 1));
        await db.SaveChangesAsync();
    }

    public IServiceScope CreateScope() => _provider!.CreateScope();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        if (_client is not null)
        {
            try
            {
                _client.DropDatabase(DbName);
            }
            catch
            {
                // best-effort cleanup
            }
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

[CollectionDefinition("mongo")]
public class MongoDatabaseCollection : ICollectionFixture<MongoDbFixture> { }
