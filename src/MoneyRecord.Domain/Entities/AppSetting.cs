namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Runtime configuration key-value store (DBD-005 T23).
/// Values are strings; each key declares its own type for validation (SET-002).
/// </summary>
public class AppSetting
{
    public int Id { get; private set; }

    /// <summary>
    /// Owning shop for shop-scoped overrides (M11 isolation); null = platform default.
    /// Effective value resolution: shop override first, global fallback.
    /// </summary>
    public long? ShopId { get; private set; }

    /// <summary>Stable API contract key, e.g. 'shopName', 'txnAmountCap'.</summary>
    public string Key { get; private set; } = default!;

    public string Value { get; private set; } = default!;

    /// <summary>'string' | 'int' | 'json' — drives SET-002 per-key validation.</summary>
    public string ValueType { get; private set; } = default!;

    /// <summary>Sensitive keys require confirmSensitive=true on update (SET-002).</summary>
    public bool IsSensitive { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public long? UpdatedByUserId { get; private set; }

    private AppSetting() { } // EF Core

    /// <summary>Public for design-time HasData seeds (Infrastructure assembly). Global default row.</summary>
    public AppSetting(int id, string key, string value, string valueType,
        bool isSensitive, IClock clock)
    {
        Id = id;
        Key = key;
        Value = value;
        ValueType = valueType;
        IsSensitive = isSensitive;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Shop-scoped override row — Id assigned by DB identity.</summary>
    public static AppSetting ForShop(long shopId, string key, string value,
        string valueType, bool isSensitive, IClock clock)
        => new()
        {
            ShopId = shopId,
            Key = key,
            Value = value,
            ValueType = valueType,
            IsSensitive = isSensitive,
            UpdatedAtUtc = clock.UtcNow
        };

    public void Update(string newValue, long? actorUserId, IClock clock)
    {
        Value = newValue;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = clock.UtcNow;
    }
}
