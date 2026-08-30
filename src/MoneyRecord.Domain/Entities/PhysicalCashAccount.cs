namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Singleton physical cash pool row per shop (DBD-005 T08, BR-001, M11 multi-shop).
/// Id = ShopId — one cash pool row per tenant.
/// </summary>
public class PhysicalCashAccount
{
    public int Id { get; private set; }

    /// <summary>Id doubles as ShopId in multi-tenant mode (one cash pool per shop).</summary>
    public long ShopId => Id;

    /// <summary>Cached cash balance; truth = Σ(CashLedgerEntries) signed.</summary>
    public long CurrentCashBalance { get; private set; }

    public DateTime? LastReconciledAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public long? UpdatedByUserId { get; private set; }

    private PhysicalCashAccount() { } // EF Core

    public static PhysicalCashAccount CreateForShop(long shopId, long openingBalance, IClock clock) =>
        new()
        {
            Id = (int)shopId,
            CurrentCashBalance = openingBalance,
            UpdatedAtUtc = clock.UtcNow
        };

    public static PhysicalCashAccount CreateSingleton(long openingBalance, IClock clock) =>
        CreateForShop(SingletonId, openingBalance, clock);

    /// <summary>Legacy constant — now means "the default/first shop" id.</summary>
    public const int SingletonId = 1;

    /// <summary>Stamp reconciliation time (called by ReconciliationService).</summary>
    public void StampReconciled(DateTime utcNow)
    {
        LastReconciledAtUtc = utcNow;
    }

    /// <summary>T4 adjustment apply — caller holds UPDLOCK and has passed BR-034 guard.</summary>
    public void ApplyAdjustment(LedgerDirection direction, long amount,
        long actorUserId, IClock clock)
    {
        CurrentCashBalance = direction == LedgerDirection.Increase
            ? CurrentCashBalance + amount
            : CurrentCashBalance - amount;
        UpdatedAtUtc = clock.UtcNow;
        UpdatedByUserId = actorUserId;
    }
}
