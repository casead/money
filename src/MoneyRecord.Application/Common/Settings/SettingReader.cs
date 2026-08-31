using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Application.Common.Settings;

/// <summary>
/// M11 — effective setting resolution: shop override first, global default fallback.
/// A shop's values NEVER leak into another shop's calculations.
/// </summary>
public static class SettingReader
{
    /// <summary>Effective raw value for a key in the caller's shop scope.</summary>
    public static async Task<string?> EffectiveAsync(IMoneyRecordDbContext db, string key,
        long? shopId, CancellationToken ct)
    {
        // Load matching settings into memory for MongoDB compatibility
        // (boolean expressions in OrderBy are not translatable).
        var candidates = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == key && (s.ShopId == shopId || s.ShopId == null))
            .ToListAsync(ct);

        return candidates
            .OrderByDescending(s => s.ShopId.HasValue) // shop override wins
            .Select(s => s.Value)
            .FirstOrDefault();
    }

    /// <summary>Effective integer value; 0 when unset/unparseable.</summary>
    public static async Task<long> EffectiveIntAsync(IMoneyRecordDbContext db, string key,
        long? shopId, CancellationToken ct)
    {
        var raw = await EffectiveAsync(db, key, shopId, ct);
        return long.TryParse(raw, out var v) ? v : 0;
    }
}
