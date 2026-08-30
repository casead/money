using FluentAssertions;
using MediatR;
using MongoDB.Driver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Domain.Entities;
using MoneyRecord.Infrastructure.Persistence;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// TC-100b/c generalized + M7 acceptance (BLUE-010):
///   Zero BalanceAfter chain gaps in soak data
///   Drift flag E2E: injected cache drift is detected by reconciliation
/// </summary>
[Collection("mongo")]
public class ReconciliationIntegrationTests
{
    private readonly MongoDbFixture _fx;

    public ReconciliationIntegrationTests(MongoDbFixture fx) => _fx = fx;

    private async Task<long> CreateAccountAsync(ISender sender, long openingFloat)
    {
        var acc = await sender.Send(new CreateWalletAccountCommand(
            1, $"Recon IT {Guid.NewGuid():N}"[..16], null, openingFloat));
        return acc.Value!.Id;
    }

    // ---- Soak: mixed traffic -> reconciliation must be CLEAN with ZERO chain gaps ----

    [Fact]
    public async Task Reconcile_MixedTraffic_ZeroChainGaps_AllClean()
    {
        long accountId;
        using (var scope = _fx.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            accountId = await CreateAccountAsync(sender, 200_000);

            // deterministic mini-soak: ins + outs on one account
            for (var i = 0; i < 6; i++)
                (await sender.Send(new CreateCashInCommand
                {
                    IdempotencyKey = Guid.NewGuid(),
                    CustomerName = "Soak", CustomerPhone = "09111100011",
                    WalletAccountId = accountId, Amount = 10_000,
                    FeePaidVia = "cash"
                })).IsSuccess.Should().BeTrue();

            for (var i = 0; i < 3; i++)
                (await sender.Send(new CreateCashOutCommand
                {
                    IdempotencyKey = Guid.NewGuid(),
                    CustomerName = "Soak", CustomerPhone = "09111100022",
                    WalletAccountId = accountId, Amount = 5_000,
                    FeePaidVia = "cash"
                })).IsSuccess.Should().BeTrue();
        }

        using var assertScope = _fx.CreateScope();
        var svc = assertScope.ServiceProvider.GetRequiredService<ReconciliationService>();

        var result = await svc.RunAsync();

        result.IsClean.Should().BeTrue(
            "soak writes are atomic -- no drift and zero chain gaps expected");
        result.ChainGapCount.Should().Be(0);
    }

    // ---- Drift injection: corrupt the CACHE only -> reconciliation flags it (E2E) ----

    [Fact]
    public async Task Reconcile_InjectedCacheDrift_FlagsMismatch()
    {
        const long skimmingAmount = 77_700;
        long accountId;
        using (var scope = _fx.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            accountId = await CreateAccountAsync(sender, 150_000);
        }

        // Corrupt the CACHE via MongoDB driver directly (simulates out-of-band cache corruption; the ledger stays authoritative).
        using (var corruptScope = _fx.CreateScope())
        {
            var mongoDb = corruptScope.ServiceProvider.GetRequiredService<IMongoDatabase>();
            var collection = mongoDb.GetCollection<WalletAccount>("walletAccounts");
            var filter = Builders<WalletAccount>.Filter.Eq(a => a.Id, accountId);
            var update = Builders<WalletAccount>.Update.Inc(a => a.CurrentFloatBalance, -skimmingAmount);
            await collection.UpdateOneAsync(filter, update);
        }

        using var assertScope = _fx.CreateScope();
        var svc = assertScope.ServiceProvider.GetRequiredService<ReconciliationService>();

        var result = await svc.RunAsync();

        result.IsClean.Should().BeFalse("cache tampering must surface as a drift");
        var drift = result.Drifts.Should().ContainSingle(d => d.Scope.StartsWith("wallet:")
            && d.Scope.Contains(accountId.ToString())).Subject;
        drift.Difference.Should().Be(-skimmingAmount);

        // BAL-002 integrityFlag surfaces the same problem to API consumers.
        using var queryScope = _fx.CreateScope();
        var sender2 = queryScope.ServiceProvider.GetRequiredService<ISender>();
        var balances = await sender2.Send(
            new MoneyRecord.Application.Balances.Queries.GetWalletBalancesQuery());
        balances.Value!.Accounts.Single(a => a.AccountId == accountId)
            .IntegrityFlag.Should().Be("MISMATCH");
    }

    // ---- Chain verifier unit-ish path: direct gap counting via service ----

    [Fact]
    public async Task Reconcile_CleanLedgers_ChainGapsZero_LastReconciledStamped()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await CreateAccountAsync(sender, 50_000);
        var db = scope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();

        var before = (await db.PhysicalCashAccounts.AsNoTracking().SingleAsync())
            .LastReconciledAtUtc;

        await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync();

        await db.ClearTrackedEntitiesAsync();
        var after = (await db.PhysicalCashAccounts.AsNoTracking().SingleAsync())
            .LastReconciledAtUtc;
        after.Should().NotBeNull();
        after!.Value.Should().BeAfter(before ?? DateTime.MinValue);
    }
}
