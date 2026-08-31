using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Balances.Queries;

/// <summary>Shared integrity computation: cache vs Σ(signed ledger).
/// Cash scope = entries created by users of the same shop (M11 isolation).</summary>
internal static class BalanceIntegrity
{
    public static async Task<(long LedgerSum, string? Flag)> ComputeCashAsync(
        IMoneyRecordDbContext db, long cachedBalance, long? shopId, CancellationToken ct)
    {
        var shopUserIds = await db.Users.AsNoTracking()
            .Where(u => u.ShopId == shopId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var entries = await db.CashLedgerEntries.AsNoTracking()
            .Where(e => shopUserIds.Contains(e.CreatedByUserId))
            .ToListAsync(ct);

        var inc = entries.Where(e => e.Direction == LedgerDirection.Increase)
            .Sum(e => e.Amount);
        var dec = entries.Where(e => e.Direction == LedgerDirection.Decrease)
            .Sum(e => e.Amount);
        var ledgerSum = IntegrityCheck.SignedSum(inc, dec);
        return (ledgerSum, IntegrityCheck.Flag(cachedBalance, ledgerSum));
    }

    public static async Task<(long LedgerSum, string? Flag)> ComputeWalletAsync(
        IMoneyRecordDbContext db, long accountId, long cachedBalance, CancellationToken ct)
    {
        var sums = await db.WalletLedgerEntries.AsNoTracking()
            .Where(e => e.WalletAccountId == accountId)
            .GroupBy(e => e.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync(ct);
        var inc = sums.Where(s => s.Direction == LedgerDirection.Increase)
            .Select(s => s.Total).FirstOrDefault();
        var dec = sums.Where(s => s.Direction == LedgerDirection.Decrease)
            .Select(s => s.Total).FirstOrDefault();
        var ledgerSum = IntegrityCheck.SignedSum(inc, dec);
        return (ledgerSum, IntegrityCheck.Flag(cachedBalance, ledgerSum));
    }
}

/// <summary>BAL-001 — physical cash balance + health.</summary>
public sealed record GetCashBalanceQuery : IRequest<Result<CashBalanceResponse>>;

public sealed record CashBalanceResponse(
    long Balance,
    DateTime? LastEntryAtUtc,
    DateTime? LastReconciledAtUtc,
    string? IntegrityFlag);

public sealed class GetCashBalanceQueryHandler
    : IRequestHandler<GetCashBalanceQuery, Result<CashBalanceResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetCashBalanceQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<CashBalanceResponse>> Handle(GetCashBalanceQuery request,
        CancellationToken ct)
    {
        var cash = await _db.PhysicalCashAccounts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == _currentUser.ShopId, ct);

        var cashBalance = cash?.CurrentCashBalance ?? 0;

        var shopUserIds = await _db.Users.AsNoTracking()
            .Where(u => u.ShopId == _currentUser.ShopId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var lastEntry = await _db.CashLedgerEntries.AsNoTracking()
            .Where(e => shopUserIds.Contains(e.CreatedByUserId))
            .OrderByDescending(e => e.Id)
            .Select(e => (DateTime?)e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var entries = await _db.CashLedgerEntries.AsNoTracking()
            .Where(e => shopUserIds.Contains(e.CreatedByUserId))
            .ToListAsync(ct);

        var inc = entries.Where(e => e.Direction == LedgerDirection.Increase)
            .Sum(e => e.Amount);
        var dec = entries.Where(e => e.Direction == LedgerDirection.Decrease)
            .Sum(e => e.Amount);
        var ledgerSum = IntegrityCheck.SignedSum(inc, dec);
        var flag = IntegrityCheck.Flag(cashBalance, ledgerSum);

        return Result<CashBalanceResponse>.Success(new CashBalanceResponse(
            cashBalance, lastEntry, cash?.LastReconciledAtUtc, flag));
    }
}

/// <summary>BAL-002 — all active wallet balances + totalFloat aggregate.</summary>
public sealed record GetWalletBalancesQuery : IRequest<Result<WalletBalancesResponse>>;

public sealed record WalletBalanceItem(
    long AccountId,
    string ProviderCode,
    string AccountMasked,
    string AccountName,
    long Balance,
    string? IntegrityFlag,
    string? ProviderLogoUrl);

public sealed record WalletBalancesResponse(
    IReadOnlyList<WalletBalanceItem> Accounts,
    long TotalFloat);

public sealed class GetWalletBalancesQueryHandler
    : IRequestHandler<GetWalletBalancesQuery, Result<WalletBalancesResponse>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetWalletBalancesQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<WalletBalancesResponse>> Handle(GetWalletBalancesQuery request,
        CancellationToken ct)
    {
        var accounts = await _db.WalletAccounts.AsNoTracking()
            .Where(a => a.IsActive && !a.IsDeleted && a.ShopId == _currentUser.ShopId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        var providerIds = accounts.Select(a => a.WalletProviderId).Distinct().ToList();
        var providers = await _db.WalletProviders.AsNoTracking()
            .Where(p => providerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Code, p.LogoUrl), ct);

        var orderedAccounts = accounts
            .OrderBy(a => providers.TryGetValue(a.WalletProviderId, out var _) ? 0 : 1)
            .ThenBy(a => a.Id)
            .ToList();

        var accountIds = orderedAccounts.Select(a => a.Id).ToList();
        var ledgerEntries = await _db.WalletLedgerEntries.AsNoTracking()
            .Where(e => accountIds.Contains(e.WalletAccountId))
            .ToListAsync(ct);

        var ledgerMap = new Dictionary<long, (long Inc, long Dec)>();
        foreach (var e in ledgerEntries)
        {
            if (!ledgerMap.TryGetValue(e.WalletAccountId, out var acc))
                acc = (0, 0);
            if (e.Direction == LedgerDirection.Increase)
                acc = (e.Amount + acc.Inc, acc.Dec);
            else
                acc = (acc.Inc, e.Amount + acc.Dec);
            ledgerMap[e.WalletAccountId] = acc;
        }

        var items = new List<WalletBalanceItem>(orderedAccounts.Count);
        foreach (var a in orderedAccounts)
        {
            var providerCode = providers.TryGetValue(a.WalletProviderId, out var pc) ? pc.Code : "???";
            var providerLogo = providers.TryGetValue(a.WalletProviderId, out var pl) ? pl.LogoUrl : null;
            var (inc, dec) = ledgerMap.TryGetValue(a.Id, out var v) ? v : (0L, 0L);
            var ledgerSum = IntegrityCheck.SignedSum(inc, dec);
            var flag = IntegrityCheck.Flag(a.CurrentFloatBalance, ledgerSum);
            items.Add(new WalletBalanceItem(
                a.Id, providerCode,
                CreateWalletAccountCommandHandler.Mask(a.AccountNumber),
                a.AccountName, a.CurrentFloatBalance, flag, providerLogo));
        }

        return Result<WalletBalancesResponse>.Success(new WalletBalancesResponse(
            items, items.Sum(i => i.Balance)));
    }
}
