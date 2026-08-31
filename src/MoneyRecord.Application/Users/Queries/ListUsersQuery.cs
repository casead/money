using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Application.Users.Queries;

/// <summary>
/// USR-001 — staff directory (FR-008). Params: page/pageSize, sortBy(username|fullName|createdAtUtc),
/// sortDir, isActive?, search?, shopId? (SuperAdmin only — platform-wide view).
/// </summary>
public sealed record ListUsersQuery(
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDir,
    bool? IsActive,
    string? Search,
    long? ShopId = null) : IRequest<Result<PagedResult<UserListItem>>>;

public sealed record UserListItem(
    long Id,
    string Username,
    string FullName,
    string? Phone,
    string RoleCode,
    bool IsActive,
    DateTime? LastLoginAtUtc,
    long? ShopId = null,
    string? ShopName = null);

/// <summary>S-A03 pagination bounds; sort whitelist enforced at handler level.</summary>
public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(0)
            .WithMessage("Page 1 ကျော် ရွေးပါ။");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(0, PagedResult<UserListItem>.MaxPageSize)
            .WithMessage($"PageSize သည် 0–{PagedResult<UserListItem>.MaxPageSize} ကြား ဖြစ်ရမည်။");
        RuleFor(x => x.SortBy)
            .Must(v => v is null ||
                       new[] { "username", "fullName", "createdAtUtc" }.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortBy သည် username|fullName|createdAtUtc သာ ဖြစ်ရမည်။");
        RuleFor(x => x.SortDir)
            .Must(v => v is null ||
                       new[] { "asc", "desc" }.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortDir သည် asc|desc သာ ဖြစ်ရမည်။");
    }
}

public sealed class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, Result<PagedResult<UserListItem>>>
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListUsersQueryHandler(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private static readonly string[] AllowedSortFields = ["username", "fullname", "createdatutc"];

    public async Task<Result<PagedResult<UserListItem>>> Handle(ListUsersQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1
            ? PagedResult<UserListItem>.DefaultPageSize
            : Math.Min(request.PageSize, PagedResult<UserListItem>.MaxPageSize);

        // Tenant scope (M11): ShopAdmin manages own shop's users only;
        // SuperAdmin (ShopId=null) sees the platform-wide directory, optionally
        // filtered to one shop via request.ShopId.
        var query = _db.Users.AsNoTracking()
            .AsQueryable();

        if (_currentUser.ShopId is { } ownShopId)
        {
            query = query.Where(u => u.ShopId == ownShopId);
        }
        else if (request.ShopId is { } filterShopId)
        {
            query = query.Where(u => u.ShopId == filterShopId);
        }

        if (request.IsActive is { } active)
            query = query.Where(u => u.IsActive == active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u =>
                u.Username.Contains(term) || u.FullName.Contains(term));
        }

        var totalItems = await query.CountAsync(ct);

        var sortBy = NormalizeSort(request.SortBy);
        var descending = !string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Domain.Entities.User> ordered = (sortBy, descending) switch
        {
            ("username", false) => query.OrderBy(u => u.Username),
            ("username", true) => query.OrderByDescending(u => u.Username),
            ("fullname", false) => query.OrderBy(u => u.FullName),
            ("fullname", true) => query.OrderByDescending(u => u.FullName),
            (_, false) => query.OrderBy(u => u.CreatedAtUtc),
            (_, true) => query.OrderByDescending(u => u.CreatedAtUtc)
        };

        var users = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var roleIds = users.Select(u => u.RoleId).Distinct().ToList();
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Code })
            .ToDictionaryAsync(r => r.Id, r => r.Code, ct);

        var shopIds = users.Where(u => u.ShopId.HasValue).Select(u => u.ShopId!.Value).Distinct().ToList();
        var shops = shopIds.Count > 0
            ? await _db.Shops.Where(s => shopIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct)
            : new Dictionary<long, string>();

        var items = users.Select(u => new UserListItem(
            u.Id,
            u.Username,
            u.FullName,
            u.Phone,
            roles.TryGetValue(u.RoleId, out var rc) ? rc : "Staff",
            u.IsActive,
            u.LastLoginAtUtc,
            u.ShopId,
            u.ShopId.HasValue && shops.TryGetValue(u.ShopId.Value, out var sn) ? sn : null))
            .ToList();

        return Result<PagedResult<UserListItem>>.Success(
            PagedResult<UserListItem>.Create(items, totalItems, page, pageSize));
    }

    /// <summary>Whitelist per API-007 §1.3 — unknown values fall back to createdAtUtc.</summary>
    internal static string NormalizeSort(string? sortBy)
    {
        var candidate = (sortBy ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return AllowedSortFields.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? candidate.ToLowerInvariant()
            : "createdatutc";
    }
}
