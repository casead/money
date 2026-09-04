using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Transactions.Queries;

/// <summary>
/// TXN-005 — filtered, paginated txn list (IX-03 composite path).
/// Default window = today (Yangon BusinessDate). Profit fields stripped for Staff.
/// </summary>
public sealed record ListTransactionsQuery(
    int Page,
    int PageSize,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? ProviderId,
    long? WalletAccountId,
    byte? TypeId,
    byte? StatusId,
    long? CreatedByUserId,
    long? MinAmount,
    long? MaxAmount,
    string? SortBy,
    string? SortDir) : IRequest<Result<PagedResult<TransactionListRow>>>;

public sealed record TransactionListRow(
    string TxnNo,
    DateTime OccurredAtUtc,
    string TypeName,
    string? CustomerNameSnapshot,
    string? CustomerPhoneMasked,
    string ProviderCode,
    long Amount,
    long FeeAmount,
    bool ShowProfitFields,
    long ProfitAmount,
    string Status);

public sealed class ListTransactionsQueryValidator : AbstractValidator<ListTransactionsQuery>
{
    public ListTransactionsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(0, PagedResult<TransactionListRow>.MaxPageSize);
        RuleFor(x => x.TypeId)
            .Must(t => t is null || t is 1 or 2).WithMessage("typeId 1=CashIn|2=CashOut");
        RuleFor(x => x.StatusId)
            .Must(s => s is null or >= 1 and <= 4).WithMessage("statusId 1..4");
        RuleFor(x => x.SortBy)
            .Must(v => v is null ||
                       new[] { "occurredAtUtc", "amount" }.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortBy သည် occurredAtUtc|amount သာ ဖြစ်ရမည်။");
        RuleFor(x => x.SortDir)
            .Must(v => v is null || new[] { "asc", "desc" }.Contains(v, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class ListTransactionsQueryHandler
    : IRequestHandler<ListTransactionsQuery, Result<PagedResult<TransactionListRow>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public ListTransactionsQueryHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<TransactionListRow>>> Handle(
        ListTransactionsQuery request, CancellationToken ct)
    {
        var showProfit = ProfitVisibility.ShowProfit(_currentUser);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > PagedResult<TransactionListRow>.MaxPageSize
            ? PagedResult<TransactionListRow>.DefaultPageSize
            : request.PageSize;

        var from = request.DateFrom ?? _clock.TodayYangon; // default: today
        var to = request.DateTo ?? from;

        // Tenant scope: SuperAdmin (ShopId=null) sees nothing shop-side (M11).
        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.ShopId == _currentUser.ShopId
                        && t.BusinessDate >= from && t.BusinessDate <= to);

        if (request.ProviderId is { } pid)
            query = query.Where(t => t.WalletProviderId == pid);
        if (request.WalletAccountId is { } aid)
            query = query.Where(t => t.WalletAccountId == aid);
        if (request.TypeId is { } tid)
            query = query.Where(t => (byte)t.Type == tid);
        if (request.StatusId is { } sid)
            query = query.Where(t => (byte)t.Status == sid);
        if (request.CreatedByUserId is { } uid)
            query = query.Where(t => t.CreatedByUserId == uid);
        if (request.MinAmount is { } min)
            query = query.Where(t => t.Amount >= min);
        if (request.MaxAmount is { } max)
            query = query.Where(t => t.Amount <= max);

        var descending = !string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var byAmount = string.Equals(request.SortBy, "amount", StringComparison.OrdinalIgnoreCase);

        var total = await query.CountAsync(ct);

        var ordered = (byAmount, descending) switch
        {
            (true, true) => query.OrderByDescending(t => t.Amount),
            (true, false) => query.OrderBy(t => t.Amount),
            (_, true) => query.OrderByDescending(t => t.OccurredAtUtc),
            (_, false) => query.OrderBy(t => t.OccurredAtUtc)
        };

        var txns = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var providerIds = txns.Select(t => t.WalletProviderId).Distinct().ToList();
        var providers = providerIds.Count > 0
            ? await _db.WalletProviders.AsNoTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, ct)
            : new Dictionary<int, string>();

        var rows = txns.Select(t => new TransactionListRow(
            t.TxnNo,
            t.OccurredAtUtc,
            t.Type.ToString(),
            t.CustomerNameSnapshot,
            Domain.Common.MyanmarPhone.Mask(t.CustomerPhoneSnapshot ?? ""),
            providers.TryGetValue(t.WalletProviderId, out var pc) ? pc : "???",
            t.Amount,
            t.FeeAmount,
            false,
            0,
            t.Status.ToString()))
            .ToList();

        // Role-aware profit stripping (schema-level assert target TC-700b).
        var items = rows.Select(r => r with
        {
            ShowProfitFields = showProfit,
            ProfitAmount = showProfit ? r.FeeAmount : 0 // v1 commission=0 → profit=fee
        }).ToList();

        return Result<PagedResult<TransactionListRow>>.Success(
            PagedResult<TransactionListRow>.Create(items, total, page, pageSize));
    }
}
