namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>Ledger movement direction (DBD T14/T15): 1=Debit(+), 2=Credit(âˆ’).</summary>
public enum LedgerDirection : byte
{
    Increase = 1,
    Decrease = 2
}

/// <summary>What caused the ledger entry: 1=Txn, 2=Adjustment.</summary>
public enum LedgerSourceType : byte
{
    Transaction = 1,
    Adjustment = 2
}

/// <summary>
/// APPEND-ONLY physical cash movement (DBD-005 T14). Never updated/deleted â€”
/// DB grants + triggers enforce immutability at DB-9; app exposes no mutation path.
/// </summary>
public class CashLedgerEntry
{
    public long Id { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public long Amount { get; private set; }

    /// <summary>Running balance after this entry â€” chain integrity checked by invariant daemon.</summary>
    public long BalanceAfter { get; private set; }

    public LedgerSourceType SourceType { get; private set; }

    public long? TransactionId { get; private set; }

    public long? CashAdjustmentId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    private CashLedgerEntry() { } // EF Core

    /// <summary>Exactly one source ref must be non-null (XOR constraint).</summary>
    public static CashLedgerEntry ForAdjustment(long adjustmentId, LedgerDirection direction,
        long amount, long balanceAfter, long actorUserId, IClock clock)
        => new()
        {
            Direction = direction,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = LedgerSourceType.Adjustment,
            CashAdjustmentId = adjustmentId,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };

    public static CashLedgerEntry ForTransactionCore(long transactionId, LedgerDirection direction,
        long amount, long balanceAfter, long actorUserId, DateTime occurredUtc)
        => new()
        {
            Direction = direction,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = LedgerSourceType.Transaction,
            TransactionId = transactionId,
            CreatedAtUtc = occurredUtc,
            CreatedByUserId = actorUserId
        }; // wired by the txn engine in M6
}

/// <summary>
/// APPEND-ONLY wallet float movement per account (DBD-005 T15).
/// </summary>
public class WalletLedgerEntry
{
    public long Id { get; private set; }

    public long WalletAccountId { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public long Amount { get; private set; }

    public long BalanceAfter { get; private set; }

    public LedgerSourceType SourceType { get; private set; }

    public long? TransactionId { get; private set; }

    public long? FloatAdjustmentId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    private WalletLedgerEntry() { } // EF Core

    public static WalletLedgerEntry ForAdjustment(long walletAccountId, long adjustmentId,
        LedgerDirection direction, long amount, long balanceAfter,
        long actorUserId, IClock clock)
        => new()
        {
            WalletAccountId = walletAccountId,
            Direction = direction,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = LedgerSourceType.Adjustment,
            FloatAdjustmentId = adjustmentId,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };

    public static WalletLedgerEntry ForTransactionCore(long walletAccountId, long transactionId,
        LedgerDirection direction, long amount, long balanceAfter,
        long actorUserId, DateTime occurredUtc)
        => new()
        {
            WalletAccountId = walletAccountId,
            Direction = direction,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = LedgerSourceType.Transaction,
            TransactionId = transactionId,
            CreatedAtUtc = occurredUtc,
            CreatedByUserId = actorUserId
        };
}

