using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Customers.Common;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Real lifetime aggregates from the Transactions ledger (replaces the v1
/// zero-stub): per-type totals, occurrence counts and last-activity instant —
/// COMPLETED transactions only (Cancelled/Reversed never count).
/// </summary>
public sealed class CustomerTransactionStatsService : ICustomerTransactionStats
{
    private readonly MoneyRecordDbContext _db;

    public CustomerTransactionStatsService(MoneyRecordDbContext db) => _db = db;

    public async Task<CustomerLifetimeStats> GetAsync(long customerId, CancellationToken ct)
    {
        // Load into memory for MongoDB compatibility (GroupBy on enum not translatable).
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId
                        && t.Status == TransactionStatus.Completed)
            .ToListAsync(ct);

        var grouped = rows.GroupBy(t => t.Type)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count(),
                Total = g.Sum(t => t.Amount),
                Last = g.Max(t => t.OccurredAtUtc)
            }).ToList();

        var cashIn = grouped.FirstOrDefault(r => r.Type == TransactionType.CashIn);
        var cashOut = grouped.FirstOrDefault(r => r.Type == TransactionType.CashOut);

        return new CustomerLifetimeStats(
            cashIn?.Total ?? 0,
            cashOut?.Total ?? 0,
            cashIn?.Count ?? 0,
            cashOut?.Count ?? 0,
            grouped.Count == 0 ? null : grouped.Max(r => r.Last));
    }
}
