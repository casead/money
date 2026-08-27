namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// Dedicated cancellation record (DBD-005 T12) — one per txn (UQ TransactionId).
/// Traceability beyond the status column: who, when, why.
/// </summary>
public class TransactionCancellation
{
    public long Id { get; private set; }

    public long TransactionId { get; private set; }

    /// <summary>Mandatory correction reason (5–300 chars, BR-022).</summary>
    public string Reason { get; private set; } = default!;

    public DateTime CancelledAtUtc { get; private set; }

    public long CancelledByUserId { get; private set; }

    private TransactionCancellation() { } // EF Core

    public static TransactionCancellation Create(long transactionId, string reason,
        long actorUserId, DateTime utcNow)
        => new()
        {
            TransactionId = transactionId,
            Reason = reason.Trim(),
            CancelledByUserId = actorUserId,
            CancelledAtUtc = utcNow
        };
}

/// <summary>
/// Original ↔ mirror link with metadata (DBD-005 T13).
/// MirrorTxnId UNIQUE — one reversal per original (BR-027 terminal protection).
/// </summary>
public class TransactionReversal
{
    public long Id { get; private set; }

    public long OriginalTxnId { get; private set; }

    public long MirrorTxnId { get; private set; }

    public string Reason { get; private set; } = default!;

    public DateTime ReversedAtUtc { get; private set; }

    public long ReversedByUserId { get; private set; }

    private TransactionReversal() { } // EF Core

    public static TransactionReversal Create(long originalTxnId, long mirrorTxnId,
        string reason, long actorUserId, DateTime utcNow)
        => new()
        {
            OriginalTxnId = originalTxnId,
            MirrorTxnId = mirrorTxnId,
            Reason = reason.Trim(),
            ReversedByUserId = actorUserId,
            ReversedAtUtc = utcNow
        };
}
