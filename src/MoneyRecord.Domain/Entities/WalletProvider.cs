namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Wallet provider catalog (DBD-005 T06) — extensible without schema change (ARCH §26).
/// Seeds: (1,'WAVE','Wave Money'), (2,'KBZPAY','KBZPay').
/// </summary>
public class WalletProvider
{
    public int Id { get; private set; }

    /// <summary>Uppercase unique code, e.g. 'WAVE'.</summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? LogoUrl { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    private WalletProvider() { } // EF Core

    /// <param name="id">0 = DB identity; explicit values only for seed data.</param>
    public WalletProvider(string code, string name, string? logoUrl, int displayOrder, int id = 0)
    {
        if (id != 0) Id = id;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        LogoUrl = logoUrl;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public void Update(string? name, string? logoUrl, int? displayOrder)
    {
        if (name is not null) Name = name.Trim();
        if (logoUrl is not null) LogoUrl = logoUrl;
        if (displayOrder is not null) DisplayOrder = displayOrder.Value;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void Delete(long userId)
    {
        IsDeleted = true;
    }
}
