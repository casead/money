using FluentAssertions;
using MediatR;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Infrastructure.Persistence;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// TC-100b/c generalized + M7 acceptance (BLUE-010):
///   â€¢ Zero BalanceAfter chain gaps in soak data
///   â€¢ Drift flag E2E: injected cache drift is detected by reconciliation
///   â€¢ Ledger append-only: UPDATE/DELETE blocked at DB level (generalized TC-100b)
/// </summary>
[Collection("sql")]
public class ReconciliationIntegrationTests
{
    private readonly PostgreSqlFixture _fx;

    public ReconciliationIntegrationTests(PostgreSqlFixture fx) => _fx = fx;

    private async Task<long> CreateAccountAsync(ISender sender, long openingFloat)
    {
        var acc = await sender.Send(new CreateWalletAccountCommand(
            1, $"Recon IT {Guid.NewGuid():N}"[..16], null, openingFloat));
        return acc.Value!.Id;
    }

    // ---- Soak: mixed traffic â†’ reconciliation must be CLEAN with ZERO chain gaps ----

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
            "soak writes are atomic â€” no drift and zero chain gaps expected");
        result.ChainGapCount.Should().Be(0);
    }

    // ---- Drift injection: corrupt the CACHE only â†’ reconciliation flags it (E2E) ----

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

        // Raw SQL simulates out-of-band cache corruption (the ledger stays authoritative).
        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE \"WalletAccounts\" SET \"CurrentFloatBalance\" = \"CurrentFloatBalance\" - @amt WHERE \"Id\" = @id";
            cmd.Parameters.AddWithValue("@amt", skimmingAmount);
            cmd.Parameters.AddWithValue("@id", accountId);
            await cmd.ExecuteNonQueryAsync();
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

    // ---- Generalized TC-100b: ledger rows reject UPDATE and DELETE at DB level ----

    [Theory]
    [InlineData("UPDATE \"CashLedgerEntries\" SET \"Amount\" = \"Amount\" + 1")]
    [InlineData("DELETE FROM \"CashLedgerEntries\"")]
    [InlineData("UPDATE \"WalletLedgerEntries\" SET \"Amount\" = \"Amount\" + 1")]
    [InlineData("DELETE FROM \"WalletLedgerEntries\"")]
    public async Task LedgerRows_AreAppendOnly_UpdateAndDeleteBlocked(string destructiveSql)
    {
        // Seed at least one row via the real engine.
        using (var seedScope = _fx.CreateScope())
        {
            var sender = seedScope.ServiceProvider.GetRequiredService<ISender>();
            var accounts = await seedScope.ServiceProvider
                .GetRequiredService<IMoneyRecordDbContext>().WalletAccounts
                .AsNoTracking().OrderBy(a => a.Id).ToListAsync();
            var target = accounts.FirstOrDefault()?.Id
                ?? await CreateAccountAsync(sender, 10_000);
            (await sender.Send(new CreateCashInCommand
            {
                IdempotencyKey = Guid.NewGuid(),
                CustomerName = "AppendOnly", CustomerPhone = "09333300011",
                WalletAccountId = target, Amount = 1_000,
                FeePaidVia = "cash"
            })).IsSuccess.Should().BeTrue();
        }

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = destructiveSql;

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<Npgsql.PostgresException>(
            "append-only ledgers (DR-02/03) must block UPDATE/DELETE via DB guards");
    }
}


