using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Customers.Queries;

/// <summary>
/// CUS-006 — Customer transaction history (A, S). Snapshot-based rows, newest
/// first, all statuses included (client renders status badges); per-shop scoped.
/// </summary>
public sealed record ListCustomerTransactionsQuery(
    long CustomerId,
    int Page,
    int PageSize,
    DateTime? DateFrom,
    DateTime? DateTo,
    int? TypeId,
    int? StatusId) : IRequest<Result<PagedResult<CustomerTransactionItem>>>;

public sealed record CustomerTransactionItem(
    long Id,
    string TxnNo,
    int TypeId,
    string TypeName,
    string ProviderCode,
    long Amount,
    int StatusId,
    string StatusName,
    DateTime OccurredAtUtc);

public sealed class ListCustomerTransactionsQueryHandler
    : IRequestHandler<ListCustomerTransactionsQuery, Result<PagedResult<CustomerTransactionItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListCustomerTransactionsQueryHandler(IMoneyRecordDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<CustomerTransactionItem>>> Handle(
        ListCustomerTransactionsQuery request, CancellationToken ct)
    {
        // Per-shop isolation: cross-tenant ids resolve to NotFound.
        var exists = await _db.Customers
            .AnyAsync(c => c.Id == request.CustomerId
                           && c.ShopId == _currentUser.ShopId, ct);
        if (!exists)
            return Result<PagedResult<CustomerTransactionItem>>.Failure(
                ErrorCodes.NotFound, "Customer ရှာမတွေ့ပါ။");

        // M6+: real history from the immutable ledger — newest first, all
        // statuses (client badges Cancelled/Reversed), shop-scoped by tenant.
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > PagedResult<CustomerTransactionItem>.MaxPageSize
            ? PagedResult<CustomerTransactionItem>.DefaultPageSize
            : request.PageSize;

        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.CustomerId == request.CustomerId
                        && t.ShopId == _currentUser.ShopId);

        if (request.DateFrom is { } from)
            query = query.Where(t => t.OccurredAtUtc >= from);
        if (request.DateTo is { } to)
            query = query.Where(t => t.OccurredAtUtc <= to);
        if (request.TypeId is { } typeId)
            query = query.Where(t => (int)t.Type == typeId);
        if (request.StatusId is { } statusId)
            query = query.Where(t => (int)t.Status == statusId);

        var totalItems = await query.CountAsync(ct);

        var txns = await query
            .OrderByDescending(t => t.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var providerIds = txns.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var items = txns.Select(t => new CustomerTransactionItem(
                t.Id,
                t.TxnNo,
                (int)t.Type,
                t.Type.ToString(),
                providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???",
                t.Amount,
                (int)t.Status,
                t.Status.ToString(),
                t.OccurredAtUtc))
            .ToList();

        return Result<PagedResult<CustomerTransactionItem>>.Success(
            PagedResult<CustomerTransactionItem>.Create(items, totalItems, page, pageSize));
    }
}
