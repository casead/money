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
    public static Task<string?> EffectiveAsync(IMoneyRecordDbContext db, string key,
        long? shopId, CancellationToken ct)
        => db.AppSettings.AsNoTracking()
            .Where(s => s.Key == key && (s.ShopId == shopId || s.ShopId == null))
            .OrderByDescending(s => s.ShopId != null) // shop override wins
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

    /// <summary>Effective integer value; 0 when unset/unparseable.</summary>
    public static async Task<long> EffectiveIntAsync(IMoneyRecordDbContext db, string key,
        long? shopId, CancellationToken ct)
    {
        var raw = await EffectiveAsync(db, key, shopId, ct);
        return long.TryParse(raw, out var v) ? v : 0;
    }
}
