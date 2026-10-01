using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Users.Commands;
using MoneyRecord.Application.Users.Queries;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Rbac;
using MoneyRecord.Domain.Entities;
using MoneyRecord.Infrastructure;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// M11 tenant isolation (C5-C7): a shop-scoped admin must NOT read / update /
/// status-change / password-reset users belonging to another shop (IDOR across
/// tenants — ids are sequential and enumerable). SuperAdmin (ShopId null) stays
/// unrestricted, matching CreateUser/ListUsers platform behavior.
/// </summary>
[Collection("mongo")]
public class UserTenantIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbFixture _fx;
    private long _crossShopUserId;
    private long _ownShopUserId;
    private string _crossShopUsername = "";

    public UserTenantIntegrationTests(MongoDbFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        using var scope = _fx.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Cross-shop target in shop 2: inserted directly — the fixture ambient is a
        // shop-1 admin and the guarded CreateUser path would (correctly) refuse.
        _crossShopUsername = $"it-other-{Random.Shared.Next(100000, 999999)}";
        var cross = User.Create(_crossShopUsername, "stub::pw", "Other Shop Staff",
            roleId: RolePermissionRegistry.StaffRoleId, actorUserId: 0, clock, shopId: 2);
        db.Users.Add(cross);

        var own = await db.Users.AsNoTracking()
            .FirstAsync(u => u.Username == "it-staff");
        _ownShopUserId = own.Id;

        await db.SaveChangesAsync();
        _crossShopUserId = cross.Id;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // ---- cross-tenant: every user-management surface returns FORBIDDEN ----

    [Fact]
    public async Task TC500a_CrossShop_Actors_AllSurfaces_ReturnForbidden()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var read = await sender.Send(new GetUserDetailsQuery(_crossShopUserId));
        read.IsSuccess.Should().BeFalse();
        read.ErrorCode.Should().Be(ErrorCodes.Forbidden, "cross-tenant read must be blocked");

        var update = await sender.Send(new UpdateUserCommand(
            _crossShopUserId, "Renamed By Intruder", null, null));
        update.IsSuccess.Should().BeFalse();
        update.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        var status = await sender.Send(new SetUserStatusCommand(_crossShopUserId, false));
        status.IsSuccess.Should().BeFalse();
        status.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        var reset = await sender.Send(new ResetUserPasswordCommand(
            _crossShopUserId, "Str0ng!Passw0rd#2026"));
        reset.IsSuccess.Should().BeFalse();
        reset.ErrorCode.Should().Be(ErrorCodes.Forbidden,
            "password reset is account takeover — must never pass cross-tenant");
    }

    // ---- control: same-shop management keeps working (no regression) ----

    [Fact]
    public async Task TC500b_SameShop_Targets_StillManaged()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var read = await sender.Send(new GetUserDetailsQuery(_ownShopUserId));
        read.IsSuccess.Should().BeTrue();
        read.Value!.Username.Should().Be("it-staff");

        var update = await sender.Send(new UpdateUserCommand(
            _ownShopUserId, "IT Staff", null, null));
        update.IsSuccess.Should().BeTrue();
    }

    // ---- SuperAdmin (platform) stays unrestricted ----

    [Fact]
    public async Task TC500c_SuperAdmin_CanManageAnyShopUser()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MoneyRecord"] = _fx.ConnectionString,
                ["MongoDb:DatabaseName"] = _fx.DbName
            }).Build());
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(
            RolePermissionRegistry.SuperAdminRoleId));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var read = await sender.Send(new GetUserDetailsQuery(_crossShopUserId));
        read.IsSuccess.Should().BeTrue();
        read.Value!.Username.Should().Be(_crossShopUsername);

        var update = await sender.Send(new UpdateUserCommand(
            _crossShopUserId, "Updated By Platform", null, null));
        update.IsSuccess.Should().BeTrue();
    }
}
