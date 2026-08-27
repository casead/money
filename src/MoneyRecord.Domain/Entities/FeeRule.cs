namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;

/// <summary>Fee rule calculation model (DBD T19): 1=Flat, 2=Percent, 3=Tiered.</summary>
public enum FeeCalculationType : byte
{
    Flat = 1,
    Percent = 2,
    Tiered = 3 // schema-ready; calculator support deferred per DD-06 [F]
}

/// <summary>
/// Effective-dated fee calculation rule per provider (DBD-005 T19, FR-028, BR-012).
/// In-force/expired rules are immutable — superseded by new effective-dated rules.
/// </summary>
public class FeeRule
{
    public int Id { get; private set; }

    public int WalletProviderId { get; private set; }

    public FeeCalculationType CalculationType { get; private set; }

    /// <summary>Required when type=Flat. CHECK > 0.</summary>
    public long? FlatAmount { get; private set; }

    /// <summary>Required when type=Percent. DECIMAL(5,4), e.g. 0.5000 = 0.5%. CHECK (0,100].</summary>
    public decimal? PercentValue { get; private set; }

    /// <summary>Percent floor (optional).</summary>
    public long? MinFee { get; private set; }

    /// <summary>Percent cap (optional). CHECK >= MinFee when both present.</summary>
    public long? MaxFee { get; private set; }

    public DateTime EffectiveFromUtc { get; private set; }

    /// <summary>NULL = open-ended.</summary>
    public DateTime? EffectiveToUtc { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public long? ModifiedByUserId { get; private set; }

    public WalletProvider WalletProvider { get; private set; } = default!;

    private FeeRule() { } // EF Core

    /// <summary>FEE-002 create with calc-type-specific validation (API §9 validation row).</summary>
    public static FeeRule Create(int walletProviderId, FeeCalculationType calculationType,
        long? flatAmount, decimal? percentValue, long? minFee, long? maxFee,
        DateTime effectiveFromUtc, long actorUserId, IClock clock)
    {
        switch (calculationType)
        {
            case FeeCalculationType.Flat when flatAmount is not > 0:
                throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                    "FLAT rule အတွက် flatFee > 0 လိုအပ်ပါသည်။");
            case FeeCalculationType.Percent when percentValue is not ( > 0 and <= 100):
                throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                    "PERCENT rule အတွက် percentRate သည် (0,100] အတွင်း ရှိရမည်။");
            case FeeCalculationType.Tiered:
                throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                    "TIERED rules များကို ဤဗားရှင်းတွင် မထောက်ပံ့သေးပါ။ (DD-06)");
        }

        if (maxFee is not null && minFee is not null && maxFee < minFee)
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "maxFee သည် minFee ထက် နည်းခွင့် မရှိပါ။");

        return new FeeRule
        {
            WalletProviderId = walletProviderId,
            CalculationType = calculationType,
            FlatAmount = flatAmount,
            PercentValue = percentValue,
            MinFee = minFee,
            MaxFee = maxFee,
            EffectiveFromUtc = effectiveFromUtc,
            IsActive = true,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
    }

    /// <summary>
    /// FEE-003 guard: only NOT-YET-EFFECTIVE rules editable (API §10 — in-force/expired
    /// rules immutable to preserve historical calc integrity). Also closes the window
    /// when re-scheduling forward.
    /// </summary>
    public void Reschedule(DateTime effectiveFromUtc, DateTime? effectiveToUtc,
        long actorUserId, DateTime utcNow)
    {
        EnsureNotYetEffective(utcNow);
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
        Touch(actorUserId, utcNow);
    }

    /// <summary>Edits the amount parameters of a NOT-YET-EFFECTIVE rule only.</summary>
    public void ReviseParameters(long? flatAmount, decimal? percentValue,
        long? minFee, long? maxFee, long actorUserId, DateTime utcNow)
    {
        EnsureNotYetEffective(utcNow);
        if (CalculationType == FeeCalculationType.Flat && flatAmount is not > 0)
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "FLAT rule အတွက် flatFee > 0 လိုအပ်ပါသည်။");
        if (CalculationType == FeeCalculationType.Percent && percentValue is not ( > 0 and <= 100))
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "PERCENT rule အတွက် percentRate သည် (0,100] အတွင်း ရှိရမည်။");
        if (maxFee is not null && minFee is not null && maxFee < minFee)
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "maxFee သည် minFee ထက် နည်းခွင့် မရှိပါ။");
        FlatAmount = flatAmount;
        PercentValue = percentValue;
        MinFee = minFee;
        MaxFee = maxFee;
        Touch(actorUserId, utcNow);
    }

    public void Deactivate(long actorUserId, DateTime utcNow) => IsActive = false;

    private void EnsureNotYetEffective(DateTime utcNow)
    {
        if (utcNow >= EffectiveFromUtc)
            throw new ConflictStateException(
                "စတင်အာဏာတည်ဆဲ (သို့) သက်တမ်းကုန်ဆုံးပြီး rule ကို ပြင်ခွင့် မရှိပါ။ (IMMUTABLE_RULE)");
    }

    private void Touch(long actorUserId, DateTime utcNow)
    {
        ModifiedAtUtc = utcNow;
        ModifiedByUserId = actorUserId;
    }
}
