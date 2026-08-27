using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// TC-600 automated suite (real SQL Server, real transactions, real UPDLOCKs).
/// Golden examples replay BRL Â§4.C/Â§4.D; storm covers TC-600d concurrent semantics.
/// </summary>
[Collection("sql")]
public class TxnEngineIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fx;

    public TxnEngineIntegrationTests(PostgreSqlFixture fx) => _fx = fx;

    private long _accountId;
    private long _cashBaseline;
    private int _txnCountBaseline;
    private const long OpeningFloat = 500_000;
    private const long OpeningCashTopUp = 300_000;

    public async Task InitializeAsync()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var acc = await sender.Send(new CreateWalletAccountCommand(
            1, "Wave IT", $"0977{Random.Shared.Next(1000000, 9999999)}", OpeningFloat));
        _accountId = acc.Value!.Id;

        await sender.Send(new AdjustBalanceCommand(
            "cash", null, "INCREASE", OpeningCashTopUp, "opening cash for txn tests", null));

        using var s2 = _fx.CreateScope();
        var db = s2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        _cashBaseline = await db.PhysicalCashAccounts.AsNoTracking().SingleAsync()
            .ContinueWith(t => t.Result.CurrentCashBalance);
        _txnCountBaseline = await db.Transactions.CountAsync();
    }

    /// <summary>LOCK_TIMEOUT (409) is valid backpressure — clients retry (API-007 §13.1).</summary>
    private static async Task<Result<TxnReceiptResponse>> RetryIn(
        ISender sender, Func<CreateCashInCommand> factory, int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await sender.Send(factory()); }
            catch (LockTimeoutException) when (attempt < maxAttempts) { await Task.Delay(150 * attempt); }
        }
    }

    private static async Task<Result<TxnReceiptResponse>> RetryOut(
        ISender sender, Func<CreateCashOutCommand> factory, int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await sender.Send(factory()); }
            catch (LockTimeoutException) when (attempt < maxAttempts) { await Task.Delay(150 * attempt); }
        }
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    private CreateCashInCommand In(long amount, Guid? key = null) => new()
    {
        IdempotencyKey = key ?? Guid.NewGuid(),
        CustomerName = "Daw Hla Hla",
        CustomerPhone = "09770001112",
        WalletAccountId = _accountId,
        Amount = amount,
        FeePaidVia = "cash"
    };

    private CreateCashOutCommand Out(long amount, Guid? key = null) => new()
    {
        IdempotencyKey = key ?? Guid.NewGuid(),
        CustomerName = "U Kyaw",
        CustomerPhone = "09887766554",
        WalletAccountId = _accountId,
        Amount = amount,
        FeePaidVia = "cash"
    };

    private async Task<(long Cash, long Float)> ReadBalancesAsync()
    {
        using var scope = _fx.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var cash = await db.PhysicalCashAccounts.AsNoTracking().SingleAsync();
        var acc = await db.WalletAccounts.AsNoTracking()
            .SingleAsync(a => a.Id == _accountId);
        return (cash.CurrentCashBalance, acc.CurrentFloatBalance);
    }

    // ---- TC-600a: BRL Â§4.C.3 golden example ----

    [Fact]
    public async Task TC600a_CashIn_GoldenExample_MovesBalancesExactly()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(In(100_000));
        result.IsSuccess.Should().BeTrue();

        var receipt = result.Value!;
        receipt.TxnNo.Should().StartWith("TXN-");
        receipt.Status.Should().Be("Completed");
        receipt.Amount.Should().Be(100_000);
        receipt.FeeAmount.Should().Be(0);            // zero-fee engine default (TC-600h)
        receipt.ShowProfitFields.Should().BeTrue();  // admin actor
        receipt.ProfitAmount.Should().Be(0);

        var (cash, float_) = await ReadBalancesAsync();
        cash.Should().Be(_cashBaseline + 100_000);    // cash â†‘ amount
        float_.Should().Be(OpeningFloat - 100_000);   // float â†“ amount

        // Exactly TWO transaction-sourced ledger entries with chained BalanceAfter
        using var scope2 = _fx.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var txnRow = await db.Transactions.AsNoTracking()
            .SingleAsync(t => t.TxnNo == receipt.TxnNo);

        var cashEntry = await db.CashLedgerEntries.AsNoTracking()
            .SingleAsync(e => e.TransactionId == txnRow.Id);
        cashEntry.Direction.Should().Be(LedgerDirection.Increase);
        cashEntry.BalanceAfter.Should().Be(cash);

        var walletEntry = await db.WalletLedgerEntries.AsNoTracking()
            .SingleAsync(e => e.TransactionId == txnRow.Id);
        walletEntry.WalletAccountId.Should().Be(_accountId);
        walletEntry.Direction.Should().Be(LedgerDirection.Decrease);
        walletEntry.BalanceAfter.Should().Be(float_);
    }

    // ---- TC-600b: cash-out mirror ----

    [Fact]
    public async Task TC600b_CashOut_MirrorsLedgerEffect()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(Out(40_000));
        result.IsSuccess.Should().BeTrue();

        var (cash, float_) = await ReadBalancesAsync();
        cash.Should().Be(_cashBaseline - 40_000);   // cash â†“
        float_.Should().Be(OpeningFloat + 40_000);  // float â†‘
    }

    [Fact]
    public async Task TC600c_InsufficientFloat_BlocksCashIn_NoPartialWrites()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var before = await ReadBalancesAsync();
        var act = () => sender.Send(In(OpeningFloat + 1));
        await act.Should().ThrowAsync<InsufficientFloatException>();

        var after = await ReadBalancesAsync();
        after.Should().Be(before); // zero partial writes

        using var scope2 = _fx.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        (await db.Transactions.CountAsync()).Should().Be(_txnCountBaseline);
    }

    [Fact]
    public async Task TC600c_InsufficientCash_BlocksCashOut()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var act = () => sender.Send(Out(_cashBaseline + 1));
        await act.Should().ThrowAsync<InsufficientCashException>();
    }

    // ---- TC-600d: sequential replay ----

    [Fact]
    public async Task TC600d_RetrySameKey_ReplaysOriginalReceipt_OneRowOnly()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var key = Guid.NewGuid();

        var first = await sender.Send(In(25_000, key));
        first.IsSuccess.Should().BeTrue();

        var retry = await sender.Send(In(25_000, key));
        retry.IsSuccess.Should().BeTrue();
        retry.Value!.TxnNo.Should().Be(first.Value!.TxnNo);
        retry.Value.IsReplay.Should().BeTrue();
        first.Value.IsReplay.Should().BeFalse();

        using var scope2 = _fx.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        (await db.Transactions.CountAsync(t => t.IdempotencyKey == key)).Should().Be(1);
    }

    // ---- TC-600e ----

    [Fact]
    public async Task TC600e_SameKeyDifferentPayload_ThrowsDuplicateRequest()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var key = Guid.NewGuid();

        (await sender.Send(In(10_000, key))).IsSuccess.Should().BeTrue();

        var act = () => sender.Send(In(20_000, key));
        await act.Should().ThrowAsync<DuplicateRequestException>();
    }

    // ---- Storm: identical-key CONCURRENT submissions ----

    [Fact]
    public async Task TC600d_ReplayStorm_IdenticalKeyConcurrent_ExactlyOneTxn_AllSucceed()
    {
        const int concurrency = 12;
        var key = Guid.NewGuid();

        var tasks = new List<Task<Result<TxnReceiptResponse>>>();
        for (var i = 0; i < concurrency; i++)
        {
            var scope = _fx.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var cmd = In(7_000, key);
            tasks.Add(Task.Run(async () =>
            {
                try { return await sender.Send(cmd); }
                finally { scope.Dispose(); }
            }));
        }

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.IsSuccess).Should().Be(concurrency,
            "UPDLOCK-first reservation serializes losers into replays");

        results.Select(r => r.Value!.TxnNo).Distinct()
            .Should().ContainSingle("exactly one transaction row may exist");

        var (cash, float_) = await ReadBalancesAsync();
        float_.Should().Be(OpeningFloat - 7_000);      // ONE debit only
        cash.Should().Be(_cashBaseline + 7_000);        // ONE credit only

        using var scope2 = _fx.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        (await db.Transactions.CountAsync(t => t.IdempotencyKey == key)).Should().Be(1);
    }

    // ---- TC-600f-lite: mixed concurrent distinct-key burst on one account ----

    [Fact]
    public async Task TC600f_ConcurrentMixedBurst_ConservesInvariantsExactly()
    {
        const int ins = 10, outs = 5;
        const long amt = 1_000;

        var tasks = new List<Task<long>>();
        for (var i = 0; i < ins; i++)
        {
            var scope = _fx.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var r = await RetryIn(sender, () => In(amt));
                    return r.IsSuccess ? 1L : 0L;
                }
                finally { scope.Dispose(); }
            }));
        }
        for (var i = 0; i < outs; i++)
        {
            var scope = _fx.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var r = await RetryOut(sender, () => Out(amt));
                    return r.IsSuccess ? 1L : 0L;
                }
                finally { scope.Dispose(); }
            }));
        }
        var outcomes = await Task.WhenAll(tasks);
        var okCount = outcomes.Sum();

        var (cash, float_) = await ReadBalancesAsync();
        okCount.Should().Be(ins + outs, "all submissions eventually succeed via retry");
        cash.Should().Be(_cashBaseline + (ins * amt) - (outs * amt));   // conservation
        float_.Should().Be(OpeningFloat - (ins * amt) + (outs * amt));

        using var assertScope = _fx.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IMoneyRecordDbContext>();
        var sums = await db.WalletLedgerEntries.AsNoTracking()
            .Where(e => e.WalletAccountId == _accountId && e.SourceType == LedgerSourceType.Transaction)
            .GroupBy(e => e.Direction)
            .Select(g => new { g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync();
        var inc = sums.FirstOrDefault(s => s.Key == LedgerDirection.Increase)?.Total ?? 0;
        var dec = sums.FirstOrDefault(s => s.Key == LedgerDirection.Decrease)?.Total ?? 0;
        // Engine semantics: Cash In ⇒ wallet Decrease; Cash Out ⇒ wallet Increase.
        // So the transaction-sourced ledger delta must equal the float-cache delta
        // since account creation (OpeningFloat): no drift between Σledger and cache.
        (inc - dec).Should().Be(float_ - OpeningFloat,
            "Σtransaction-ledger delta must equal float-cache delta (no drift)");
    }
}


