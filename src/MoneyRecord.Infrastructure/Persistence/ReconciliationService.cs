using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Result of one reconciliation pass (DR-01/DR-08).
/// </summary>
public sealed record LedgerDriftReport(
    string Scope,
    long CachedBalance,
    long LedgerSignedSum,
    long Difference,
    bool HasChainGap);

public sealed record ReconciliationResult(
    DateTime RanAtUtc,
    IReadOnlyList<LedgerDriftReport> Drifts,
    int ChainGapCount)
{
    public bool IsClean => Drifts.Count == 0 && ChainGapCount == 0;
}

/// <summary>
/// M7 core: cache-vs-ledger reconciliation + BalanceAfter chain verification.
/// Read-only against business data; writes LastReconciledAtUtc + audit row only.
/// </summary>
public sealed class ReconciliationService
{
    private readonly MoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly MoneyRecord.Application.Common.Interfaces.ICurrentUser _currentUser;

    public ReconciliationService(MoneyRecordDbContext db, IClock clock,
        MoneyRecord.Application.Common.Interfaces.ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    private int CashRowId => (int)(_currentUser.ShopId
        ?? throw new InvalidOperationException("Shop context မရှိပါ။"));

    public async Task<ReconciliationResult> RunAsync(CancellationToken ct = default)
    {
        var drifts = new List<LedgerDriftReport>();

        // ---- Physical cash: Σ(ledger) vs cache (per-shop scope) ----
        var cash = await _db.PhysicalCashAccounts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CashRowId, ct);
        if (cash is not null)
        {
            var (cashSum, cashFlag) = await SumCashLedgerAsync(ct);
            if (cashFlag == IntegrityCheck.Mismatch)
                drifts.Add(new LedgerDriftReport("cash", cash.CurrentCashBalance, cashSum,
                    cash.CurrentCashBalance - cashSum, false));
        }

        // ---- Every wallet account: Σ(per-account ledger) vs cache ----
        var accounts = await _db.WalletAccounts.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Select(a => new { a.Id, a.AccountName, a.CurrentFloatBalance })
            .ToListAsync(ct);
        foreach (var account in accounts)
        {
            var sums = await _db.WalletLedgerEntries.AsNoTracking()
                .Where(e => e.WalletAccountId == account.Id)
                .GroupBy(e => e.Direction)
                .Select(g => new { g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync(ct);
            var grouped = sums.Select(s => (Key: s.Key, Total: s.Total)).ToList();
            var ledgerSum = IntegrityCheck.SignedSum(
                Sum(grouped, LedgerDirection.Increase),
                Sum(grouped, LedgerDirection.Decrease));
            if (IntegrityCheck.Flag(account.CurrentFloatBalance, ledgerSum) is not null)
                drifts.Add(new LedgerDriftReport($"wallet:{account.Id} ({account.AccountName})",
                    account.CurrentFloatBalance, ledgerSum,
                    account.CurrentFloatBalance - ledgerSum, false));
        }

        // ---- BalanceAfter chain integrity (both ledgers, per scope) ----
        var chainGaps = await VerifyChainsAsync(ct);

        // Stamp reconciliation time on the shop's cash row.
        await _db.PhysicalCashAccounts
            .Where(c => c.Id == CashRowId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReconciledAtUtc, _clock.UtcNow), ct);

        return new ReconciliationResult(_clock.UtcNow, drifts, chainGaps);
    }

    private async Task<(long Sum, string? Flag)> SumCashLedgerAsync(CancellationToken ct)
    {
        var cash = await _db.PhysicalCashAccounts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CashRowId, ct);
        // Shop-scoped: only entries created by this shop's users count (M11).
        var sums = await _db.CashLedgerEntries.AsNoTracking()
            .Where(e => _db.Users.Any(u => u.Id == e.CreatedByUserId && u.ShopId == CashRowId))
            .GroupBy(e => e.Direction)
            .Select(g => new { g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync(ct);
        var grouped = sums.Select(s => (Key: s.Key, Total: s.Total)).ToList();
        var sum = IntegrityCheck.SignedSum(Sum(grouped, LedgerDirection.Increase),
            Sum(grouped, LedgerDirection.Decrease));
        return (sum, IntegrityCheck.Flag(cash?.CurrentCashBalance ?? 0, sum));
    }

    private static long Sum(IEnumerable<(LedgerDirection Key, long Total)> source,
        LedgerDirection direction)
        => source.Where(s => s.Key == direction).Select(s => s.Total).FirstOrDefault();

    /// <summary>
    /// Chain rule per scope: first entry's BalanceAfter must equal its signed amount;
    /// each subsequent entry's BalanceAfter must equal previous +/− amount (ordered by Id).
    /// Implemented in SQL for scan efficiency; returns total gap count across all scopes.
    /// </summary>
    private async Task<int> VerifyChainsAsync(CancellationToken ct)
    {
        var gaps = 0;

        // Cash chain — shop-scoped via entry creator's shop (M11 isolation)
        var cashRows = await _db.CashLedgerEntries.AsNoTracking()
            .Where(e => _db.Users.Any(u => u.Id == e.CreatedByUserId && u.ShopId == CashRowId))
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.Direction, e.Amount, e.BalanceAfter })
            .ToListAsync(ct);
        gaps += CountChainGaps(cashRows.Count == 0 ? [] : cashRows
            .Select(r => (r.Direction, r.Amount, r.BalanceAfter)));

        // Wallet chains — grouped per account
        var walletRows = await _db.WalletLedgerEntries.AsNoTracking()
            .OrderBy(e => e.WalletAccountId).ThenBy(e => e.Id)
            .Select(e => new { e.WalletAccountId, e.Direction, e.Amount, e.BalanceAfter })
            .ToListAsync(ct);
        foreach (var group in walletRows.GroupBy(r => r.WalletAccountId))
        {
            gaps += CountChainGaps(group
                .Select(r => (r.Direction, r.Amount, r.BalanceAfter)));
        }

        return gaps;
    }

    private static int CountChainGaps(IEnumerable<(LedgerDirection Direction, long Amount, long BalanceAfter)> entries)
    {
        var gaps = 0;
        long? running = null;
        foreach (var entry in entries)
        {
            var signed = entry.Direction == LedgerDirection.Increase ? entry.Amount : -entry.Amount;
            var expected = running is null ? signed : running + signed;
            if (entry.BalanceAfter != expected)
                gaps++;
            running = entry.BalanceAfter;
        }
        return gaps;
    }
}
