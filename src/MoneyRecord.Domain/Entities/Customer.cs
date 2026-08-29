namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Registered customer registry row (DBD-005 T05). Soft-delete master data —
/// never hard deleted (BC-02). Walk-in customers get NO row here;
/// their info lives only in transaction snapshot columns (Q12 default: walk-ins allowed).
/// Tenancy (M11 per-shop isolation): each customer belongs to EXACTLY ONE shop —
/// shop A sees only its own registry, shop B only its own.
/// Source: "manual" = added via FAB, "auto" = auto-registered during transaction.
/// </summary>
public class Customer
{
    public long Id { get; private set; }

    public string FullName { get; private set; } = default!;

    /// <summary>Canonical Myanmar format (MyanmarPhone). Unique per shop among non-deleted rows.</summary>
    public string Phone { get; private set; } = default!;

    public string? Address { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Customer source: "manual" (FAB) or "auto" (transaction auto-register).</summary>
    public string Source { get; private set; } = "auto";

    /// <summary>Owning shop — customers are shop-private (per-shop isolation).</summary>
    public long ShopId { get; private set; }

    public Shop Shop { get; private set; } = default!;

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public long? ModifiedByUserId { get; private set; }

    private Customer() { } // EF Core

    public static Customer Create(string fullName, string phone,
        string? address, string? note, long actorUserId, IClock clock,
        long shopId, string source = "auto")
    {
        return new Customer
        {
            FullName = fullName.Trim(),
            Phone = phone,
            Address = address?.Trim(),
            Note = note?.Trim(),
            Source = source,
            ShopId = shopId,
            IsDeleted = false,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
    }

    /// <summary>
    /// Master-data edit (CUS-004). Historical transactions keep their own
    /// snapshots — this NEVER rewrites financial history (CF-03 resolution).
    /// </summary>
    public void UpdateProfile(string? fullName, string? phone,
        string? address, string? note, long actorUserId, IClock clock)
    {
        if (fullName is not null) FullName = fullName.Trim();
        if (phone is not null) Phone = phone;
        if (address is not null) Address = address.Trim();
        if (note is not null) Note = note.Trim();
        ModifiedAtUtc = clock.UtcNow;
        ModifiedByUserId = actorUserId;
    }
}
