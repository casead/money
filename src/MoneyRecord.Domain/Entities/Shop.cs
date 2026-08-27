namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Tenant = one agent shop (ငွေသွင်းငွေထုတ်ဆိုင်) — multi-tenancy root (M11).
/// Shop status Suspended blocks all shop-side logins/operations but preserves data.
/// </summary>
public class Shop
{
    public const int ActiveStatus = 1;
    public const int SuspendedStatus = 2;

    public long Id { get; private set; }

    /// <summary>Short unique code used for login/discovery (e.g. 'MAIN').</summary>
    public string Code { get; private set; } = default!;

    /// <summary>Display name shown in app header/receipts (Burmese allowed).</summary>
    public string Name { get; private set; } = default!;

    /// <summary>1=Active, 2=Suspended.</summary>
    public int Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    private Shop() { } // EF Core

    public Shop(long id, string code, string name, int status, DateTime createdAtUtc)
    {
        Id = id;
        Code = code;
        Name = name;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Creates a new Active shop (identity column assigns Id on save).</summary>
    public static Shop Create(string code, string name, IClock clock) => new()
    {
        Code = code.Trim().ToUpperInvariant(),
        Name = name.Trim(),
        Status = ActiveStatus,
        CreatedAtUtc = clock.UtcNow
    };

    public void Rename(string name, IClock clock)
    {
        Name = name.Trim();
        ModifiedAtUtc = clock.UtcNow;
    }

    public void Suspend(IClock clock)
    {
        Status = SuspendedStatus;
        ModifiedAtUtc = clock.UtcNow;
    }

    public void Reactivate(IClock clock)
    {
        Status = ActiveStatus;
        ModifiedAtUtc = clock.UtcNow;
    }
}
