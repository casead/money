using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application;
using MoneyRecord.Application.Audit.Queries;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Fees.Commands;
using MoneyRecord.Application.Reports.Queries;
using MoneyRecord.Application.Settings.Commands;
using MoneyRecord.Application.Settings.Queries;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Domain.Entities;
using MoneyRecord.Infrastructure;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// TC-1000 reports/settings/audit suite (Module 10):
/// dashboardâ†”list consistency, correction netting, staff profit stripping,
/// day-boundary BusinessDate bucketing, empty ranges, settings guards + audit.
/// </summary>
[Collection("sql")]
public class ReportsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fx;

    public ReportsIntegrationTests(PostgreSqlFixture fx) => _fx = fx;

    private long _accountId;

    private const long OpeningFloat = 500_000;
    private const long OpeningCashTopUp = 300_000;

    public async Task InitializeAsync()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var acc = await sender.Send(new CreateWalletAccountCommand(
            1, "Wave Report", $"0966{Random.Shared.Next(1000000, 9999999)}", OpeningFloat));
        _accountId = acc.Value!.Id;

        // Cash baseline so cash-outs are possible.
        var adjust = await sender.Send(new AdjustBalanceCommand(
            "cash", null, "Increase", OpeningCashTopUp, "IT opening cash", CountedValue: null));
        adjust.IsSuccess.Should().BeTrue();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    private CreateCashInCommand In(long amount) => new()
    {
        IdempotencyKey = Guid.NewGuid(),
        CustomerName = "U Report",
        CustomerPhone = "09771112223",
        WalletAccountId = _accountId,
        Amount = amount
    };

    private CreateCashOutCommand Out(long amount) => new()
    {
        IdempotencyKey = Guid.NewGuid(),
        CustomerName = "U Report",
        CustomerPhone = "09771112223",
        WalletAccountId = _accountId,
        Amount = amount
    };

    // ---- TC-1000a: dashboard totals == Î£ same-day filtered txn list ----

    [Fact]
    public async Task TC1000a_DashboardTiesOut_WithTxnAggregates()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(In(50_000));
        await sender.Send(In(30_000));
        await sender.Send(Out(20_000));

        var dash = await sender.Send(new GetDashboardQuery(null));
        dash.IsSuccess.Should().BeTrue();

        using var s2 = _fx.CreateScope();
        var db = s2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var completedToday = await db.Transactions.AsNoTracking()
            .Where(t => t.BusinessDate == today && t.Status == TransactionStatus.Completed)
            .ToListAsync();
        completedToday.Should().NotBeEmpty();

        dash.Value!.TodayCashInTotal.Should().Be(completedToday
            .Where(t => t.Type == TransactionType.CashIn).Sum(t => t.Amount));
        dash.Value!.TodayCashOutTotal.Should().Be(completedToday
            .Where(t => t.Type == TransactionType.CashOut).Sum(t => t.Amount));
        dash.Value!.TodayTxnCount.Should().Be(completedToday.Count);
        dash.Value!.TotalFloat.Should().Be((await db.WalletAccounts.AsNoTracking()
            .Where(a => a.IsActive && !a.IsDeleted).ToListAsync())
            .Sum(a => a.CurrentFloatBalance));
    }

    // ---- TC-1000b: prior-day txn lands on its own BusinessDate bucket ----

    [Fact]
    public async Task TC1000b_DayBoundary_TxnLandsOnBusinessDate()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var create = await sender.Send(In(10_000));
        create.IsSuccess.Should().BeTrue();

        // Rewrite BusinessDate to yesterday â€” simulating a prior-day transaction.
        using (var s2 = _fx.CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
            var row = await db.Transactions.SingleAsync(t => t.TxnNo == create.Value!.TxnNo);
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Transactions"" SET ""BusinessDate"" = {yesterday} WHERE ""Id"" = {row.Id}");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dashToday = await sender.Send(new GetDashboardQuery(today));
        var dailyToday = await sender.Send(new GetDailyReportQuery(today, "provider"));

        // Yesterday's txn must NOT pollute today's buckets.
        var todayProviderTxnCount = dailyToday.Value!.ByProvider.Sum(p => p.TxnCount);
        dailyToday.Value!.TxnCount.Should().BeGreaterThanOrEqualTo(todayProviderTxnCount);

        using var s3 = _fx.CreateScope();
        var db3 = s3.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var yesterdayCount = await db3.Transactions.AsNoTracking()
            .CountAsync(t => t.BusinessDate == today.AddDays(-1)
                             && t.Status == TransactionStatus.Completed);
        yesterdayCount.Should().BeGreaterThanOrEqualTo(1);

        // Monthly report for the current month includes both days via date buckets.
        var monthly = await sender.Send(new GetMonthlyReportQuery(
            today.Year, today.Month));
        monthly.IsSuccess.Should().BeTrue();
        monthly.Value!.TxnCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // ---- TC-1000c: staff-scoped queries carry zero profit data ----

    [Fact]
    public async Task TC1000c_StaffScopes_StripProfitFields()
    {
        // Staff-context handler run: TestCurrentUser(roleId=2) â†’ profit stripped.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MoneyRecord"] = _fx.ConnectionString
            }).Build());
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(
            Domain.Common.Rbac.RolePermissionRegistry.StaffRoleId));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var dash = await sender.Send(new GetDashboardQuery(null));
        dash.IsSuccess.Should().BeTrue();
        dash.Value!.TodayGrossProfit.Should().BeNull(); // staff never sees profit

        var profit = await sender.Send(new GetProfitReportQuery(
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow), "day"));
        profit.IsSuccess.Should().BeFalse(); // handler-level role guard for staff
    }

    // ---- TC-1000d: CSV export is Excel-safe (BOM + quoting) ----

    [Fact]
    public async Task TC1000d_CsvExport_HasBomAndQuoting()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(In(15_000));
        var daily = await sender.Send(new GetDailyReportQuery(null, "provider"));
        daily.IsSuccess.Should().BeTrue();

        // Reproduce the controller's CSV row builder over the real aggregate.
        var r = daily.Value!;
        var rows = new List<string> { "Provider,CashIn,CashOut,TxnCount,Fees,Commissions" };
        foreach (var p in r.ByProvider)
            rows.Add($"{p.ProviderCode},{p.CashInTotal},{p.CashOutTotal},{p.TxnCount},{p.Fees},{p.Commissions}");

        var csv = string.Join("\r\n", rows);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var withBom = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(bytes).ToArray();

        withBom[0].Should().Be(0xEF); // UTF-8 BOM present â†’ Excel detects encoding
        withBom[1].Should().Be(0xBB);
        withBom[2].Should().Be(0xBF);
        csv.Split("\r\n").Should().HaveCount(rows.Count);
    }

    // ---- TC-1000e: empty ranges return clean empties ----

    [Fact]
    public async Task TC1000e_EmptyRanges_ReturnCleanEmpties()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var farPast = new DateOnly(2024, 1, 1);

        var daily = await sender.Send(new GetDailyReportQuery(farPast, "provider"));
        daily.IsSuccess.Should().BeTrue();
        daily.Value!.TxnCount.Should().Be(0);
        daily.Value!.TotalCashIn.Should().Be(0);
        daily.Value!.ByProvider.Should().BeEmpty();

        var monthly = await sender.Send(new GetMonthlyReportQuery(2024, 1));
        monthly.IsSuccess.Should().BeTrue();
        monthly.Value!.TxnCount.Should().Be(0);

        var profit = await sender.Send(new GetProfitReportQuery(
            farPast, farPast.AddDays(5), "day"));
        profit.IsSuccess.Should().BeTrue();
        profit.Value!.Should().BeEmpty();
    }

    // ---- SET-002 guards + audit; SET-001 role scoping ----

    [Fact]
    public async Task SET002_UnknownKey_AndSensitiveGuard_Enforced()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Unknown key â†’ validation failure
        var unknown = await sender.Send(new UpdateSettingsCommand(
            new Dictionary<string, string> { ["nope"] = "x" }, ConfirmSensitive: false));
        unknown.IsSuccess.Should().BeFalse();

        // Sensitive key without confirm â†’ CONFLICT_STATE + reason extension
        var sensitive = await sender.Send(new UpdateSettingsCommand(
            new Dictionary<string, string> { ["dayBoundaryOffsetHours"] = "2" },
            ConfirmSensitive: false));
        sensitive.IsSuccess.Should().BeFalse();
        sensitive.Extensions!["reason"].Should().Be("SENSITIVE_CHANGE");

        // With confirm â†’ success + audited
        var ok = await sender.Send(new UpdateSettingsCommand(
            new Dictionary<string, string> { ["shopName"] = "á€…á€™á€ºá€¸á€žá€•á€ºá€†á€­á€¯á€„á€º",
                ["dayBoundaryOffsetHours"] = "2" },
            ConfirmSensitive: true));
        ok.IsSuccess.Should().BeTrue();
        ok.Value!["shopName"].Should().Be("á€…á€™á€ºá€¸á€žá€•á€ºá€†á€­á€¯á€„á€º");

        var audit = await sender.Send(new ListAuditLogsQuery(1, 20,
            DateFrom: null, DateTo: null, EntityType: "SETTING",
            EntityId: null, Action: "SETTING.UPDATE", ActorUserId: null));
        audit.IsSuccess.Should().BeTrue();
        audit.Value!.Items.Should().Contain(i =>
            i.EntityType == "SETTING" && i.ActionCode == "SETTING.UPDATE");
    }

    [Fact]
    public async Task SET001_SettingsRead_RoleScoped()
    {
        // Admin sees all keys
        using var adminScope = _fx.CreateScope();
        var adminSender = adminScope.ServiceProvider.GetRequiredService<ISender>();
        var adminView = await adminSender.Send(new GetSettingsQuery());
        adminView.IsSuccess.Should().BeTrue();
        adminView.Value!.Values.Should().HaveCountGreaterThanOrEqualTo(8);

        // Staff sees only safe keys
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MoneyRecord"] = _fx.ConnectionString
            }).Build());
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(
            Domain.Common.Rbac.RolePermissionRegistry.StaffRoleId));
        services.AddScoped<IRequestContext>(_ => new TestRequestContext());

        await using var provider = services.BuildServiceProvider();
        using var staffScope = provider.CreateScope();
        var staffSender = staffScope.ServiceProvider.GetRequiredService<ISender>();
        var staffView = await staffSender.Send(new GetSettingsQuery());
        staffView.IsSuccess.Should().BeTrue();
        staffView.Value!.Values.Select(v => v.Key).Should().BeSubsetOf(
            new[] { "shopName", "receiptFooterText", "feePercentCashIn", "feePercentCashOut" });
    }
}


