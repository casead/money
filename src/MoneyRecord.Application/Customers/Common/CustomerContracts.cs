using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common;

namespace MoneyRecord.Application.Customers.Common;

/// <summary>
/// Lifetime aggregates for CUS-003 — per-type totals, occurrence counts and
/// last-activity instant. COMPLETED transactions only (M6+ real query).
/// </summary>
public interface ICustomerTransactionStats
{
    Task<CustomerLifetimeStats> GetAsync(long customerId, CancellationToken ct);
}

public sealed record CustomerLifetimeStats(
    long TotalCashIn,
    long TotalCashOut,
    int CashInCount,
    int CashOutCount,
    DateTime? LastTxnAtUtc);

internal static class CustomerMapping
{
    public static CustomerDetailResponse ToResponse(Domain.Entities.Customer c,
        CustomerLifetimeStats stats) =>
        new(c.Id, c.FullName, c.Phone, c.Address, c.Note,
            c.IsBookmarked, c.CreatedAtUtc, c.ModifiedAtUtc, stats);
}

/// <summary>CUS-003 full profile + lifetime aggregates.</summary>
public sealed record CustomerDetailResponse(
    long Id,
    string FullName,
    string Phone,
    string? Address,
    string? Note,
    bool IsBookmarked,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    CustomerLifetimeStats Lifetime);

/// <summary>CUS-001 customer card row (txnCountLifetime arrives with M6).</summary>
public sealed record CustomerListItem(
    long Id,
    string FullName,
    string Phone,
    string? Address,
    bool IsBookmarked = false)
{
    public string MaskedPhone => MyanmarPhone.Mask(Phone);
}
