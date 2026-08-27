using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Common.Models;
using MoneyRecord.Application.Common.Settings;
using MoneyRecord.Domain.Common.Errors;

namespace MoneyRecord.Application.Settings.Commands;

/// <summary>Updatable key catalog with per-key type/range rules (API-007 SET-002).</summary>
public static class SettingCatalog
{
    public sealed record KeyRule(string Key, string ValueType, bool Sensitive, Func<string, bool> Validate);

    public static readonly IReadOnlyList<KeyRule> Rules =
    [
        new("shopName", "string", false, v => v.Length is >= 1 and <= 100),
        new("dayBoundaryOffsetHours", "int", true, v => int.TryParse(v, out var n) && n is >= 0 and <= 6),
        new("pendingExpiryMinutes", "int", false, v => int.TryParse(v, out var n) && n is >= 1 and <= 1440),
        new("duplicateWindowMinutes", "int", false, v => int.TryParse(v, out var n) && n is >= 1 and <= 60),
        new("txnAmountCap", "int", false, v => long.TryParse(v, out var n) && n > 0),
        new("lowBalanceCashThreshold", "int", false, v => long.TryParse(v, out var n) && n >= 0),
        new("lowBalanceFloatThresholdPerAccount", "int", false, v => long.TryParse(v, out var n) && n >= 0),
        // Percent-only fee engine (BR-012 v2): separate Cash-In / Cash-Out rates.
        new("feePercentCashIn", "percent", false,
            v => decimal.TryParse(v, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pIn) && pIn is >= 0 and <= 100),
        new("feePercentCashOut", "percent", false,
            v => decimal.TryParse(v, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pOut) && pOut is >= 0 and <= 100),
        new("receiptFooterText", "string", false, v => v.Length <= 200)
    ];

    public static KeyRule? Find(string key) =>
        Rules.FirstOrDefault(r => r.Key == key);

    /// <summary>Staff-visible safe keys (SET-001) — fee rates are customer-facing.</summary>
    public static readonly IReadOnlyList<string> StaffSafeKeys =
    [
        "shopName", "receiptFooterText",
        "feePercentCashIn", "feePercentCashOut"
    ];
}

/// <summary>
/// SET-002 — partial settings update (Admin only). Sensitive keys require
/// confirmSensitive=true else 409 SENSITIVE_CHANGE. Full before/after diff audited.
/// </summary>
public sealed record UpdateSettingsCommand(
    Dictionary<string, string> Values,
    bool ConfirmSensitive) : IRequest<Result<Dictionary<string, string>>>;

public sealed class UpdateSettingsCommandValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsCommandValidator()
    {
        RuleFor(x => x.Values).NotNull().NotEmpty()
            .WithMessage("values map သည် ဗလာ မဖြစ်ရပါ။");
        RuleForEach(x => x.Values).ChildRules(pairs =>
        {
            pairs.RuleFor(kv => kv.Key).MaximumLength(50);
            pairs.RuleFor(kv => kv.Value).MaximumLength(500);
        });
    }
}

public sealed class UpdateSettingsCommandHandler
    : IRequestHandler<UpdateSettingsCommand, Result<Dictionary<string, string>>>
{
    private const string SensitiveChange = "SENSITIVE_CHANGE";

    private readonly IMoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UpdateSettingsCommandHandler(IMoneyRecordDbContext db, IClock clock,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<Dictionary<string, string>>> Handle(
        UpdateSettingsCommand request, CancellationToken ct)
    {
        // Unknown keys → 400 (API-007 SET-002 §9)
        var unknown = request.Values.Keys.Where(k => SettingCatalog.Find(k) is null).ToList();
        if (unknown.Count > 0)
            return Result<Dictionary<string, string>>.Failure(ErrorCodes.ValidationFailed,
                $"မသိနိုင်တဲ့ setting key များ- {string.Join(", ", unknown)}");

        // Per-key value validation
        foreach ((var key, var value) in request.Values)
        {
            var rule = SettingCatalog.Find(key)!;
            if (!rule.Validate(value))
                return Result<Dictionary<string, string>>.Failure(ErrorCodes.ValidationFailed,
                    $"{key} ၏ တန်ဖိုး မှားယွင်းနေပါသည် ({rule.ValueType})။");
        }

        // Sensitive-key guard
        var sensitiveTouched = request.Values.Keys
            .Any(k => SettingCatalog.Find(k)!.Sensitive);
        if (sensitiveTouched && !request.ConfirmSensitive)
            return Result<Dictionary<string, string>>.FailureWith(
                ErrorCodes.ConflictState,
                "Sensitive setting ပြောင်းလဲမှုအတွက် confirmSensitive=true လိုအပ်ပါသည်။",
                new Dictionary<string, object?> { ["reason"] = SensitiveChange });

        // M11 isolation: writes go to the CALLER'S scope. ShopAdmin edits their
        // shop's override rows; SuperAdmin (ShopId null) edits platform defaults.
        var keys = request.Values.Keys.ToList();
        var settings = await _db.AppSettings
            .Where(s => keys.Contains(s.Key) && s.ShopId == _currentUser.ShopId)
            .ToDictionaryAsync(s => s.Key, s => s, ct);

        var before = new Dictionary<string, string>();
        var after = new Dictionary<string, string>();

        foreach ((var key, var newValue) in request.Values)
        {
            // Audit the EFFECTIVE previous value (shop override else global default).
            before[key] = await SettingReader.EffectiveAsync(
                _db, key, _currentUser.ShopId, ct) ?? string.Empty;
            after[key] = newValue;

            if (settings.TryGetValue(key, out var setting))
            {
                setting.Update(newValue, _currentUser.UserId, _clock);
            }
            else if (_currentUser.ShopId is long sid)
            {
                var rule = SettingCatalog.Find(key)!;
                _db.AppSettings.Add(Domain.Entities.AppSetting.ForShop(
                    sid, key, newValue, rule.ValueType, rule.Sensitive, _clock));
            }
            // SuperAdmin + missing global row: unreachable (all keys seeded); skip safely.
        }

        // EntityId column is varchar(30) — key list goes in the JSON diff instead.
        await _audit.LogAsync("SETTING.UPDATE", "SETTING", "settings",
            oldValue: JsonSerializer.Serialize(before),
            newValue: JsonSerializer.Serialize(after), ct);

        await _db.SaveChangesAsync(ct);

        return Result<Dictionary<string, string>>.Success(after);
    }
}
