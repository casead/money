namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;

/// <summary>
/// CommissionSources lookup (DBD T20B) — HOW a commission value was captured (S-A06).
/// Seed: 1=PerTxnAuto, 2=PerTxnManual, 3=PeriodicBatch, 4=Adjustment.
/// </summary>
public class CommissionSource
{
    public byte Id { get; private set; }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    private CommissionSource() { } // EF Core

    public static void Seed() { } // data seeded via configuration HasData
}

/// <summary>
/// Per-txn commission capture record (DBD T21, BR-014/015). Append-only —
/// wrong entries get corrective adjustment entries, never edits/deletes.
/// XOR: exactly one of TransactionId / BatchRef non-null.
/// </summary>
public class CommissionEntry
{
    public long Id { get; private set; }

    /// <summary>Per-txn capture path.</summary>
    public long? TransactionId { get; private set; }

    /// <summary>Periodic manual batch path (D5 mixed model).</summary>
    public string? BatchRef { get; private set; }

    /// <summary>CHECK > 0. May exceed fee — EC-11 loss visible in reports.</summary>
    public long Amount { get; private set; }

    public byte SourceId { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    public Transaction? Transaction { get; private set; }

    public CommissionSource Source { get; private set; } = default!;

    private CommissionEntry() { } // EF Core

    /// <summary>Per-txn capture (S-A06 default path, BR-014).</summary>
    public static CommissionEntry ForTransaction(long transactionId, long amount,
        byte sourceId, string? note, long actorUserId, IClock clock)
        => new()
        {
            TransactionId = transactionId,
            Amount = amount,
            SourceId = sourceId,
            Note = note?.Trim(),
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };

    /// <summary>Periodic manual batch capture (D5 secondary path).</summary>
    public static CommissionEntry ForBatch(string batchRef, long amount,
        string? note, long actorUserId, IClock clock)
        => new()
        {
            BatchRef = batchRef,
            Amount = amount,
            SourceId = 3, // PeriodicBatch
            Note = note?.Trim(),
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = actorUserId
        };
}
