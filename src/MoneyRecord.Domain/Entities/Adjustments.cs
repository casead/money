namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>Adjustment type lookup (DBD-005 T18). Seeded: CashCorrection/FloatTopUp/FloatWithdrawal.</summary>
public class AdjustmentType
{
    public byte Id { get; private set; }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    private AdjustmentType() { } // EF Core

    public AdjustmentType(byte id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    public const byte CashCorrectionId = 1;
    public const byte FloatTopUpId = 2;
    public const byte FloatWithdrawalId = 3;
}

/// <summary>Audited cash correction (DBD-005 T16) — paired with a CashLedgerEntry.</summary>
public class CashAdjustment
{
    public long Id { get; private set; }

    public byte AdjustmentTypeId { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public long Amount { get; private set; }

    public string Reason { get; private set; } = default!;

    public long BalanceAfter { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    /// <summary>Future dual-control [F].</summary>
    public long? ApprovedByUserId { get; private set; }

    private CashAdjustment() { } // EF Core

    public static CashAdjustment Create(LedgerDirection direction, long amount,
        string reason, long balanceAfter, long actorUserId, IClock clock)
        => new()
        {
            AdjustmentTypeId = AdjustmentType.CashCorrectionId,
            Direction = direction,
            Amount = amount,
            Reason = reason.Trim(),
            BalanceAfter = balanceAfter,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
}

/// <summary>Audited float top-up/withdrawal (DBD-005 T17) — paired with a WalletLedgerEntry.</summary>
public class FloatAdjustment
{
    public long Id { get; private set; }

    public long WalletAccountId { get; private set; }

    public byte AdjustmentTypeId { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public long Amount { get; private set; }

    public string Reason { get; private set; } = default!;

    public long BalanceAfter { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    private FloatAdjustment() { } // EF Core

    public static FloatAdjustment Create(long walletAccountId, LedgerDirection direction,
        long amount, string reason, long balanceAfter, long actorUserId, IClock clock)
        => new()
        {
            WalletAccountId = walletAccountId,
            AdjustmentTypeId = direction == LedgerDirection.Increase
                ? AdjustmentType.FloatTopUpId
                : AdjustmentType.FloatWithdrawalId,
            Direction = direction,
            Amount = amount,
            Reason = reason.Trim(),
            BalanceAfter = balanceAfter,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
}
