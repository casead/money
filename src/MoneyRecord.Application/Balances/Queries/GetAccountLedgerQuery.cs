using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Balances.Queries;

/// <summary>
/// Shared paged ledger projection: DESC entries with running BalanceAfter,
/// actor username, and adjustment reason resolution (ACC-003 / BAL-004 shape).
/// </summary>
public sealed record LedgerEntryItem(
    long EntryId,
    string Direction,
    long Amount,
    long BalanceAfter,
    string SourceType,
    long? TxnId,
    string? AdjustmentReason,
    DateTime OccurredAtUtc,
    string ByUserName);

internal static class LedgerPaging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (page < 1 ? 1 : page,
         pageSize is < 1 or > PagedResult<LedgerEntryItem>.MaxPageSize
             ? PagedResult<LedgerEntryItem>.DefaultPageSize
             : pageSize);
}

/// <summary>ACC-003 — wallet account ledger history.</summary>
public sealed record GetAccountLedgerQuery(
    long AccountId,
    int Page,
    int PageSize,
    DateTime? DateFrom,
    DateTime? DateTo,
    int? SourceType) : IRequest<Result<PagedResult<LedgerEntryItem>>>;

public sealed class GetAccountLedgerQueryHandler
    : IRequestHandler<GetAccountLedgerQuery, Result<PagedResult<LedgerEntryItem>>>
{
    private readonly IMoneyRecordDbContext _db;

    public GetAccountLedgerQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<PagedResult<LedgerEntryItem>>> Handle(
        GetAccountLedgerQuery request, CancellationToken ct)
    {
        if (!await _db.WalletAccounts.AnyAsync(a => a.Id == request.AccountId, ct))
            return Result<PagedResult<LedgerEntryItem>>.Failure(
                ErrorCodes.NotFound, "WalletAccount ရှာမတွေ့ပါ။");

        var (page, pageSize) = LedgerPaging.Normalize(request.Page, request.PageSize);

        var query = _db.WalletLedgerEntries.AsNoTracking()
            .Where(e => e.WalletAccountId == request.AccountId);
        if (request.DateFrom is { } from)
            query = query.Where(e => e.CreatedAtUtc >= from);
        if (request.DateTo is { } to)
            query = query.Where(e => e.CreatedAtUtc <= to);
        if (request.SourceType is { } sourceTypeFilter)
            query = query.Where(e => (int)e.SourceType == sourceTypeFilter);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id, e.Direction, e.Amount, e.BalanceAfter,
                e.SourceType, e.TransactionId, e.FloatAdjustmentId,
                e.CreatedAtUtc, e.CreatedByUserId
            })
            .ToListAsync(ct);

        var items = await ProjectWithActorAndReasonAsync(_db, rows
            .Select(r => new Row(r.Id, r.Direction, r.Amount, r.BalanceAfter, r.SourceType,
                r.TransactionId, r.FloatAdjustmentId, r.CreatedAtUtc, r.CreatedByUserId))
            .ToList(), ct);

        return Result<PagedResult<LedgerEntryItem>>.Success(
            PagedResult<LedgerEntryItem>.Create(items, total, page, pageSize));
    }

    internal sealed record Row(long Id, LedgerDirection Direction, long Amount,
        long BalanceAfter, LedgerSourceType SourceType, long? TransactionId,
        long? AdjustmentId, DateTime CreatedAtUtc, long CreatedByUserId);

    internal static async Task<List<LedgerEntryItem>> ProjectWithActorAndReasonAsync(
        IMoneyRecordDbContext db, List<Row> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var adjustmentIds = rows.Where(r => r.AdjustmentId is not null)
            .Select(r => r.AdjustmentId!.Value).ToList();
        var reasons = new Dictionary<long, string>();
        if (adjustmentIds.Count > 0)
        {
            var floatReasons = await db.FloatAdjustments.AsNoTracking()
                .Where(a => adjustmentIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Reason, ct);
            foreach (var kv in floatReasons) reasons[kv.Key] = kv.Value;
        }

        return rows.Select(r => new LedgerEntryItem(
            r.Id,
            r.Direction == LedgerDirection.Increase ? "+" : "−",
            r.Amount,
            r.BalanceAfter,
            r.SourceType == LedgerSourceType.Transaction ? "txn" : "adjustment",
            r.TransactionId,
            r.AdjustmentId is not null && reasons.TryGetValue(r.AdjustmentId.Value, out var reason)
                ? reason : null,
            r.CreatedAtUtc,
            names.GetValueOrDefault(r.CreatedByUserId, $"user:{r.CreatedByUserId}")))
            .ToList();
    }
}
