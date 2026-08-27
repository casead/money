using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Balances.Queries;

/// <summary>BAL-004 — cash ledger history (append-only, BalanceAfter chain).</summary>
public sealed record GetCashLedgerQuery(
    int Page,
    int PageSize,
    DateTime? DateFrom,
    DateTime? DateTo) : IRequest<Result<PagedResult<LedgerEntryItem>>>;

public sealed class GetCashLedgerQueryHandler
    : IRequestHandler<GetCashLedgerQuery, Result<PagedResult<LedgerEntryItem>>>
{
    private readonly IMoneyRecordDbContext _db;

    public GetCashLedgerQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<PagedResult<LedgerEntryItem>>> Handle(
        GetCashLedgerQuery request, CancellationToken ct)
    {
        var (page, pageSize) = LedgerPaging.Normalize(request.Page, request.PageSize);

        var query = _db.CashLedgerEntries.AsNoTracking();
        if (request.DateFrom is { } from)
            query = query.Where(e => e.CreatedAtUtc >= from);
        if (request.DateTo is { } to)
            query = query.Where(e => e.CreatedAtUtc <= to);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id, e.Direction, e.Amount, e.BalanceAfter,
                e.SourceType, e.TransactionId, e.CashAdjustmentId,
                e.CreatedAtUtc, e.CreatedByUserId
            })
            .ToListAsync(ct);

        // Shared projection (actor + adjustment reason resolution)
        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var adjustmentIds = rows.Where(r => r.CashAdjustmentId is not null)
            .Select(r => r.CashAdjustmentId!.Value).ToList();
        var reasons = await _db.CashAdjustments.AsNoTracking()
            .Where(a => adjustmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Reason, ct);

        var items = rows.Select(r => new LedgerEntryItem(
            r.Id,
            r.Direction == LedgerDirection.Increase ? "+" : "−",
            r.Amount,
            r.BalanceAfter,
            r.SourceType == LedgerSourceType.Transaction ? "txn" : "adjustment",
            r.TransactionId,
            r.CashAdjustmentId is not null && reasons.TryGetValue(r.CashAdjustmentId.Value, out var reason)
                ? reason : null,
            r.CreatedAtUtc,
            names.GetValueOrDefault(r.CreatedByUserId, $"user:{r.CreatedByUserId}")))
            .ToList();

        return Result<PagedResult<LedgerEntryItem>>.Success(
            PagedResult<LedgerEntryItem>.Create(items, total, page, pageSize));
    }
}

/// <summary>ACC-002 — per-account cached balance + integrity flag.</summary>
public sealed record GetAccountBalanceQuery(long AccountId)
    : IRequest<Result<AccountBalanceResponse>>;

public sealed record AccountBalanceResponse(
    long AccountId,
    long Balance,
    DateTime? LastEntryAtUtc,
    string? IntegrityFlag);

public sealed class GetAccountBalanceQueryHandler
    : IRequestHandler<GetAccountBalanceQuery, Result<AccountBalanceResponse>>
{
    private readonly IMoneyRecordDbContext _db;

    public GetAccountBalanceQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<AccountBalanceResponse>> Handle(GetAccountBalanceQuery request,
        CancellationToken ct)
    {
        var account = await _db.WalletAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, ct);
        if (account is null)
            return Result<AccountBalanceResponse>.Failure(
                ErrorCodes.NotFound, "WalletAccount ရှာမတွေ့ပါ။");

        var lastEntry = await _db.WalletLedgerEntries.AsNoTracking()
            .Where(e => e.WalletAccountId == account.Id)
            .OrderByDescending(e => e.Id)
            .Select(e => (DateTime?)e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var (_, flag) = await BalanceIntegrity.ComputeWalletAsync(
            _db, account.Id, account.CurrentFloatBalance, ct);

        return Result<AccountBalanceResponse>.Success(new AccountBalanceResponse(
            account.Id, account.CurrentFloatBalance, lastEntry, flag));
    }
}
