using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Customers.Common;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Customers.Queries;

/// <summary>
/// CUS-001 — paginated registry list (A, S). Also serves CUS-005 typeahead via
/// `search` + small pageSize (phone prefix OR name contains, <500ms @10k rows).
/// Per-shop tenancy: only the caller's own shop's customers are visible.
/// DateFrom/DateTo narrow the window (client-local day boundaries as UTC
/// instants); a `search` spans the whole registry regardless of dates.
/// </summary>
public sealed record ListCustomersQuery(
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDir,
    string? Search,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    bool IncludeDeleted = false,
    string? Source = null,
    bool? Bookmarked = null) : IRequest<Result<PagedResult<CustomerListItem>>>;

public sealed class ListCustomersQueryValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(0, PagedResult<CustomerListItem>.MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(v => v is null ||
                       new[] { "fullName", "phone", "createdAtUtc" }
                           .Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortBy သည် fullName|phone|createdAtUtc သာ ဖြစ်ရမည်။");
        RuleFor(x => x.SortDir)
            .Must(v => v is null || new[] { "asc", "desc" }.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortDir သည် asc|desc သာ ဖြစ်ရမည်။");
    }
}

public sealed class ListCustomersQueryHandler
    : IRequestHandler<ListCustomersQuery, Result<PagedResult<CustomerListItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListCustomersQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<CustomerListItem>>> Handle(
        ListCustomersQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? PagedResult<CustomerListItem>.DefaultPageSize
            : Math.Min(request.PageSize, PagedResult<CustomerListItem>.MaxPageSize);

        // Per-shop isolation (M11): shop members see ONLY their own registry.
        // Platform accounts (ShopId = null) match no rows.
        IQueryable<Domain.Entities.Customer> query = (request.IncludeDeleted
            ? _db.Customers.IgnoreQueryFilters()
            : _db.Customers)
            .Where(c => c.ShopId == _currentUser.ShopId);

        // Source filter (manual / auto)
        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(c => c.Source == request.Source);

        // Bookmark filter
        if (request.Bookmarked == true)
            query = query.Where(c => c.IsBookmarked);

        var hasSearch = !string.IsNullOrWhiteSpace(request.Search);
        if (hasSearch)
        {
            var term = request.Search!.Trim();

            // CUS-005: phone queries match by normalized PREFIX ('09 77', '+959770…').
            var digits = new string(term.Where(char.IsDigit).ToArray());
            string? phonePrefix = null;
            if (digits.Length > 0)
            {
                if (digits.StartsWith("0095", StringComparison.Ordinal))
                    phonePrefix = "0" + digits[4..];
                else if (digits.StartsWith("95", StringComparison.Ordinal) && digits.Length >= 5)
                    phonePrefix = "0" + digits[2..];
                else if (digits.StartsWith("09", StringComparison.Ordinal))
                    phonePrefix = digits;
            }

            query = phonePrefix is not null
                ? query.Where(c => c.FullName.Contains(term) || c.Phone.StartsWith(phonePrefix))
                : query.Where(c => c.FullName.Contains(term));
        }

        // Date filter: only when NOT searching. Search spans the whole registry.
        // When date range is active and bookmark filter is NOT explicitly set,
        // also include bookmarked customers who had transactions in that range
        // (even if the customer was created before the range).
        if (!hasSearch)
        {
            if (request.DateFrom.HasValue && request.DateTo.HasValue
                && request.Bookmarked != true)
            {
                var from = request.DateFrom.Value;
                var to = request.DateTo.Value;
                var shopId = _currentUser.ShopId;

                // Materialize bookmarked customer IDs from transactions first
                // (cross-collection subqueries not supported by MongoDB EF Core).
                var fromD = DateOnly.FromDateTime(from);
                var toD = DateOnly.FromDateTime(to);
                var bookmarkedTxnCustomerIds = await _db.Transactions.AsNoTracking()
                    .Where(t => t.ShopId == shopId
                                && t.BusinessDate >= fromD
                                && t.BusinessDate <= toD
                                && t.CustomerId != null)
                    .Select(t => t.CustomerId!.Value)
                    .Distinct()
                    .ToListAsync(ct);

                query = query.Where(c =>
                    (c.CreatedAtUtc >= from && c.CreatedAtUtc <= to)
                    || (c.IsBookmarked && bookmarkedTxnCustomerIds.Contains(c.Id)));
            }
            else
            {
                if (request.DateFrom.HasValue)
                    query = query.Where(c => c.CreatedAtUtc >= request.DateFrom.Value);
                if (request.DateTo.HasValue)
                    query = query.Where(c => c.CreatedAtUtc <= request.DateTo.Value);
            }
        }

        var totalItems = await query.CountAsync(ct);

        var descending = !string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var byName = string.Equals(request.SortBy, "fullname", StringComparison.OrdinalIgnoreCase);
        var byPhone = string.Equals(request.SortBy, "phone", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Domain.Entities.Customer> ordered = (byName, byPhone, descending) switch
        {
            (true, _, false) => query.OrderBy(c => c.FullName),
            (true, _, true) => query.OrderByDescending(c => c.FullName),
            (_, true, false) => query.OrderBy(c => c.Phone),
            (_, true, true) => query.OrderByDescending(c => c.Phone),
            (_, _, false) => query.OrderBy(c => c.CreatedAtUtc),
            (_, _, true) => query.OrderByDescending(c => c.CreatedAtUtc)
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItem(c.Id, c.FullName, c.Phone, c.Address, c.IsBookmarked))
            .ToListAsync(ct);

        return Result<PagedResult<CustomerListItem>>.Success(
            PagedResult<CustomerListItem>.Create(items, totalItems, page, pageSize));
    }
}
