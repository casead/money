using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Settings;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Fees.Services;

/// <summary>Resolved fee outcome: applied rule snapshot + computed amount.</summary>
public sealed record FeeResolution(long FeeAmount, int? AppliedRuleId);

/// <summary>
/// Fee calculation port (BR-012 v2 — percent-only engine). One percent rate per
/// transaction type (CashIn / CashOut) configured via AppSettings
/// ('feePercentCashIn' / 'feePercentCashOut'); integer-safe math + Half-Up
/// rounding (BRL §2.3).
/// </summary>
public interface IFeeCalculator
{
    /// <summary>Returns a 0-fee resolution when the configured rate is 0/unset.</summary>
    Task<FeeResolution> CalculateAsync(TransactionType txnType, long amount,
        CancellationToken ct);
}

public sealed class FeeCalculator : IFeeCalculator
{
    public const string CashInKey = "feePercentCashIn";
    public const string CashOutKey = "feePercentCashOut";

    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public FeeCalculator(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<FeeResolution> CalculateAsync(TransactionType txnType, long amount,
        CancellationToken ct)
    {
        var key = txnType == TransactionType.CashIn ? CashInKey : CashOutKey;
        // M11: shop's own fee rate (override) else platform default — never another shop's.
        var raw = await SettingReader.EffectiveAsync(
            _db, key, _currentUser.ShopId, ct);

        var percent = decimal.TryParse(raw, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 0m;

        return new FeeResolution(ComputePercent(amount, percent), AppliedRuleId: null);
    }

    /// <summary>
    /// amount × percent / 100 with Round Half-Up to whole kyat (BRL §2.3).
    /// Golden edge: 2.5% of 33,333 → 833.325 → 833.
    /// </summary>
    public static long ComputePercent(long amount, decimal percent)
        => (long)Math.Round(amount * percent / 100m, MidpointRounding.AwayFromZero);
}
