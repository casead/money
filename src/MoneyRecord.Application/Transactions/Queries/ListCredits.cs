using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Queries;

public sealed record ListCreditsQuery(
    int Page = 1,
    int PageSize = 20,
    string? CreditType = null,
    string? Status = null,
    string? SortBy = null,
    string? SortDir = null
) : IRequest<Result<PagedResult<CreditListItem>>>;

public sealed record CreditListItem(
    long Id,
    string TxnNo,
    string Type,
    string CreditType,
    long Amount,
    long FeeAmount,
    string CreditStatus,
    string? CustomerName,
    string? CustomerPhone,
    string ProviderCode,
    string AccountName,
    DateTime OccurredAtUtc,
    DateTime? SettledAtUtc);

public sealed class ListCreditsQueryHandler
    : IRequestHandler<ListCreditsQuery, Result<PagedResult<CreditListItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListCreditsQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<CreditListItem>>> Handle(
        ListCreditsQuery request, CancellationToken ct)
    {
        var shopId = _currentUser.ShopId;

        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == shopId && t.IsCredit);

        if (request.CreditType?.ToLowerInvariant() == "receivable")
            query = query.Where(t => t.CreditPaymentMethod == "ewallet");
        else if (request.CreditType?.ToLowerInvariant() == "payable")
            query = query.Where(t => t.CreditPaymentMethod == "cash");

        if (request.Status?.ToLowerInvariant() is { } status)
        {
            query = status switch
            {
                "pending" => query.Where(t => t.CreditStatus == CreditStatus.Pending),
                "confirmed" => query.Where(t => t.CreditStatus == CreditStatus.Confirmed),
                "settled" => query.Where(t => t.CreditStatus == CreditStatus.Settled),
                "cancelled" => query.Where(t => t.CreditStatus == CreditStatus.Cancelled),
                _ => query
            };
        }
        else
        {
            query = query.Where(t => t.CreditStatus == CreditStatus.Pending || t.CreditStatus == CreditStatus.Confirmed);
        }

        query = (request.SortBy?.ToLowerInvariant(), request.SortDir?.ToLowerInvariant()) switch
        {
            ("amount", "desc") => query.OrderByDescending(t => t.Amount),
            ("amount", _) => query.OrderBy(t => t.Amount),
            ("date", "desc") => query.OrderByDescending(t => t.OccurredAtUtc),
            _ => query.OrderBy(t => t.OccurredAtUtc)
        };

        var totalItems = await query.CountAsync(ct);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var providerIds = items.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var result = items.Select(t => new CreditListItem(
            t.Id,
            t.TxnNo,
            t.Type.ToString(),
            t.CreditPaymentMethod == "ewallet" ? "receivable" : "payable",
            t.Amount,
            t.FeeAmount,
            t.CreditStatus?.ToString() ?? "Unknown",
            t.CustomerNameSnapshot,
            t.CustomerPhoneSnapshot,
            providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???",
            "",
            t.OccurredAtUtc,
            t.SettledAtUtc)).ToList();

        return Result<PagedResult<CreditListItem>>.Success(
            PagedResult<CreditListItem>.Create(result, totalItems, request.Page, request.PageSize));
    }
}