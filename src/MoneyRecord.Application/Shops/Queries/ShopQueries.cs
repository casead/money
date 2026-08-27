using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Shops.Queries;

/// <summary>TEN-004 — paginated shop list (tenant.manage).</summary>
public sealed record ListShopsQuery(
    int Page = 1, int PageSize = 20,
    string? Search = null, int? Status = null)
    : IRequest<Result<PagedResult<ShopListItem>>>;

public sealed record ShopListItem(
    long Id, string Code, string Name, int Status,
    DateTime CreatedAtUtc);

public sealed class ListShopsQueryHandler : IRequestHandler<ListShopsQuery, Result<PagedResult<ShopListItem>>>
{
    private readonly IMoneyRecordDbContext _db;

    public ListShopsQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<PagedResult<ShopListItem>>> Handle(ListShopsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _db.Shops.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(s =>
                s.Code.Contains(request.Search) || s.Name.Contains(request.Search));
        if (request.Status is { } status)
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new ShopListItem(s.Id, s.Code, s.Name, s.Status, s.CreatedAtUtc))
            .ToListAsync(ct);

        return Result<PagedResult<ShopListItem>>.Success(
            PagedResult<ShopListItem>.Create(items, total, page, pageSize));
    }
}

/// <summary>TEN-005 — shop details.</summary>
public sealed record GetShopQuery(long Id) : IRequest<Result<ShopListItem>>;

public sealed class GetShopQueryHandler : IRequestHandler<GetShopQuery, Result<ShopListItem>>
{
    private readonly IMoneyRecordDbContext _db;

    public GetShopQueryHandler(IMoneyRecordDbContext db) => _db = db;

    public async Task<Result<ShopListItem>> Handle(GetShopQuery request, CancellationToken ct)
    {
        var shop = await _db.Shops.AsNoTracking()
            .Where(s => s.Id == request.Id)
            .Select(s => new ShopListItem(s.Id, s.Code, s.Name, s.Status, s.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return shop is null
            ? Result<ShopListItem>.Failure(ErrorCodes.NotFound, "ဆိုင် ရှာမတွေ့ပါ။")
            : Result<ShopListItem>.Success(shop);
    }
}
