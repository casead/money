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
        var sums = await db.CashLedgerEntries.AsNoTracking()
            .Where(e => db.Users.Any(u => u.Id == e.CreatedByUserId && u.ShopId == shopId))
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
        // Missing cash row (legacy shop) → 0 instead of 500; creation seeds it going forward.
        var cash = await _db.PhysicalCashAccounts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == _currentUser.ShopId, ct); // per-shop cash pool (M11)

        var lastEntry = await _db.CashLedgerEntries.AsNoTracking()
            .Where(e => _db.Users.Any(u => u.Id == e.CreatedByUserId
                                           && u.ShopId == _currentUser.ShopId))
            .OrderByDescending(e => e.Id)
            .Select(e => (DateTime?)e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var (_, flag) = await BalanceIntegrity.ComputeCashAsync(
            _db, cash?.CurrentCashBalance ?? 0, _currentUser.ShopId, ct);

        return Result<CashBalanceResponse>.Success(new CashBalanceResponse(
            cash?.CurrentCashBalance ?? 0, lastEntry, cash?.LastReconciledAtUtc, flag));
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
    string? IntegrityFlag);

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
            .OrderBy(a => a.WalletProvider.DisplayOrder).ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                ProviderCode = a.WalletProvider.Code,
                a.AccountName,
                a.AccountNumber,
                a.CurrentFloatBalance
            })
            .ToListAsync(ct);

        var items = new List<WalletBalanceItem>(accounts.Count);
        foreach (var a in accounts)
        {
            var (_, flag) = await BalanceIntegrity.ComputeWalletAsync(
                _db, a.Id, a.CurrentFloatBalance, ct);
            items.Add(new WalletBalanceItem(
                a.Id, a.ProviderCode,
                CreateWalletAccountCommandHandler.Mask(a.AccountNumber),
                a.AccountName, a.CurrentFloatBalance, flag));
        }

        return Result<WalletBalancesResponse>.Success(new WalletBalancesResponse(
            items, items.Sum(i => i.Balance)));
    }
}
