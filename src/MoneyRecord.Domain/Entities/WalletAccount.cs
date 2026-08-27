namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Shop's own provider account holding float (DBD-005 T07).
/// CurrentFloatBalance = CACHE; truth = Σ(WalletLedgerEntries) per account (ARCH §4).
/// </summary>
public class WalletAccount
{
    public long Id { get; private set; }

    /// <summary>Tenant (M11) — the shop that owns this float account.</summary>
    public long ShopId { get; private set; }

    public int WalletProviderId { get; private set; }

    public WalletProvider WalletProvider { get; private set; } = default!;

    public string AccountName { get; private set; } = default!;

    public string? AccountNumber { get; private set; }

    /// <summary>Cached float. Same-txn writes only, under UPDLOCK (BR-035).</summary>
    public long CurrentFloatBalance { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public long? ModifiedByUserId { get; private set; }

    private WalletAccount() { } // EF Core

    public static WalletAccount Create(int providerId, string accountName,
        string? accountNumber, long openingFloat, long actorUserId, IClock clock,
        long shopId)
    {
        return new WalletAccount
        {
            WalletProviderId = providerId,
            ShopId = shopId,
            AccountName = accountName.Trim(),
            AccountNumber = accountNumber?.Trim(),
            // Opening balance lands via FloatAdjustment+ledger in the handler (BR-009);
            // cache starts at the opening value so cache==ledger from birth.
            CurrentFloatBalance = openingFloat,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Soft-delete — sets IsDeleted=true and stamps modification audit.</summary>
    public void Delete(long actorUserId, IClock clock)
    {
        IsDeleted = true;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>T4 adjustment apply — caller holds UPDLOCK and has passed BR-034 guard.</summary>
    public void ApplyAdjustment(LedgerDirection direction, long amount,
        long actorUserId, IClock clock)
    {
        CurrentFloatBalance = direction == LedgerDirection.Increase
            ? CurrentFloatBalance + amount
            : CurrentFloatBalance - amount;
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }
}
