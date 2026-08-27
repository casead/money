namespace MoneyRecord.Application.Common.Models;

/// <summary>Pagination metadata per API-007 §1.1 envelope.</summary>
public sealed record PageMeta(int Page, int PageSize, int TotalItems, int TotalPages);

/// <summary>Standard paginated list result (API-007 §1.3: default 20, max 100 — S-A03).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageMeta Pagination)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalItems, int page, int pageSize)
    {
        var size = pageSize is < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var totalPages = size == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)size);
        return new PagedResult<T>(items, new PageMeta(page, size, totalItems, totalPages));
    }
}
