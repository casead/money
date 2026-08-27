namespace MoneyRecord.Domain.Entities;

using MoneyRecord.Domain.Common;
using MoneyRecord.Domain.Common.Errors;
using MoneyRecord.Domain.Common.Exceptions;

/// <summary>Lookup (DBD T09): 1=CashIn, 2=CashOut.</summary>
public enum TransactionType : byte
{
    CashIn = 1,
    CashOut = 2
}

/// <summary>Lookup (DBD T10): state machine states.</summary>
public enum TransactionStatus : byte
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3,
    Reversed = 4
}

/// <summary>
/// How the customer fee is collected (BR-012 extension): Cash = fee added to the
/// physical cash pool; WalletFloat = fee paid from the customer's mobile wallet,
/// added to the shop's wallet-provider float.
/// </summary>
public enum FeePaidVia : byte
{
    Cash = 1,
    WalletFloat = 2
}

/// <summary>
/// Immutable financial transaction (DBD-005 T11, DR-04).
/// Created directly in COMPLETED state (v1 has no async provider flow — PENDING reserved).
/// Corrections happen ONLY via Cancel/Reverse records (M8) — never by editing this row.
/// </summary>
public class Transaction
{
    public long Id { get; private set; }

    /// <summary>Human-readable 'TXN-YYYY-00001' (unique, from TxnNoSeq).</summary>
    public string TxnNo { get; private set; } = default!;

    public TransactionType Type { get; private set; }

    public TransactionStatus Status { get; private set; }

    /// <summary>Principal moved. CHECK > 0.</summary>
    public long Amount { get; private set; }

    /// <summary>Snapshot — customer-facing revenue (BR-012). CHECK >= 0.</summary>
    public long FeeAmount { get; private set; }

    public int? FeeRuleId { get; private set; }

    /// <summary>D7/BR-013: override flag surfaced in reports + audit.</summary>
    public bool FeeOverridden { get; private set; }

    /// <summary>Immutable snapshot — how the fee was collected (cash vs wallet float).</summary>
    public FeePaidVia FeePaidVia { get; private set; }

    /// <summary>Provider commission cost snapshot (BR-014). CHECK >= 0.</summary>
    public long CommissionAmount { get; private set; }

    public long? CustomerId { get; private set; }

    /// <summary>Immutable copy at completion time — registry edits never rewrite history (CF-03).</summary>
    public string CustomerNameSnapshot { get; private set; } = default!;

    public string CustomerPhoneSnapshot { get; private set; } = default!;

    public int WalletProviderId { get; private set; }

    public WalletProvider WalletProvider { get; private set; } = default!;

    public long WalletAccountId { get; private set; }

    public WalletAccount WalletAccount { get; private set; } = default!;

    /// <summary>Tenant snapshot (M11) — denormalized from wallet account for row-level scoping.</summary>
    public long ShopId { get; private set; }

    public string? Note { get; private set; }

    public string? ReferenceNo { get; private set; }

    /// <summary>BR-031: unique logical submission key (UQ).</summary>
    public Guid IdempotencyKey { get; private set; }

    // ---- Correction chain (populated from M8) ----

    public long? ReversedByTxnId { get; private set; }

    public long? ReversalOfTxnId { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public long? CancelledByUserId { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }

    public long? ReversedByUserId { get; private set; }

    public string? ReversalReason { get; private set; }

    // ---- Timing ----

    /// <summary>Asia/Yangon calendar day — report grouping key (A-02).</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Server-authoritative instant (EC-09).</summary>
    public DateTime OccurredAtUtc { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private Transaction() { } // EF Core

    /// <summary>T1 insert — COMPLETED at birth with full immutable snapshots.</summary>
    public static Transaction Complete(
        string txnNo, TransactionType type, long amount, long feeAmount, bool feeOverridden,
        int? feeRuleId, FeePaidVia feePaidVia,
        long? customerId, string customerNameSnapshot, string customerPhoneSnapshot,
        int walletProviderId, long walletAccountId, Guid idempotencyKey,
        string? note, string? referenceNo, long createdByUserId, IClock clock,
        long shopId)
    {
        if (amount <= 0)
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "Amount သည် ၀ ထက် ကြီးရမည်။");
        if (feeAmount < 0)
            throw new BusinessRuleException(ErrorCodes.InvalidOperation,
                "Fee သည် အနှုတ် မဖြစ်ရ။");

        var now = clock.UtcNow;
        return new Transaction
        {
            TxnNo = txnNo,
            Type = type,
            Status = TransactionStatus.Completed,
            Amount = amount,
            FeeAmount = feeAmount,
            FeeOverridden = feeOverridden,
            FeeRuleId = feeRuleId,
            FeePaidVia = feePaidVia,
            CommissionAmount = 0, // commission capture retrofits in the fee module (M9/BR-014)
            CustomerId = customerId,
            CustomerNameSnapshot = customerNameSnapshot.Trim(),
            CustomerPhoneSnapshot = customerPhoneSnapshot,
            WalletProviderId = walletProviderId,
            WalletAccountId = walletAccountId,
            ShopId = shopId,
            Note = note?.Trim(),
            ReferenceNo = referenceNo,
            IdempotencyKey = idempotencyKey,
            BusinessDate = clock.TodayYangon,
            OccurredAtUtc = now,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now
        };
    }

    // ---- State machine guards (enforced again by M8 handlers + DB-9 trigger) ----

    public bool IsTerminal => Status is TransactionStatus.Cancelled or TransactionStatus.Reversed;

    public bool IsCompleted => Status == TransactionStatus.Completed;

    public long GrossProfit => FeeAmount - CommissionAmount; // BR-016

    /// <summary>Handler calls AFTER acquiring the txn-row UPDLOCK (EC-03 race safety).</summary>
    public void MarkCancelled(long actorUserId, string reason, DateTime utc)
    {
        EnsureTransitionAllowed();
        Status = TransactionStatus.Cancelled;
        CancelledAtUtc = utc;
        CancelledByUserId = actorUserId;
        CancellationReason = reason;
    }

    /// <summary>
    /// Handler calls AFTER UPDLOCK. mirrorTxnId links original → mirror
    /// (BR-025/BR-26 traceable chain); terminal guard blocks reversal-of-reversal (BR-027).
    /// </summary>
    public void MarkReversed(long actorUserId, string reason, DateTime utc, long? mirrorTxnId)
    {
        EnsureTransitionAllowed();
        Status = TransactionStatus.Reversed;
        ReversedAtUtc = utc;
        ReversedByUserId = actorUserId;
        ReversalReason = reason;
        ReversedByTxnId = mirrorTxnId;
    }

    /// <summary>Mirror-side back-link: mirror.ReversalOfTxnId = original.Id.</summary>
    public void LinkAsReversalOf(long originalTxnId) => ReversalOfTxnId = originalTxnId;

    private void EnsureTransitionAllowed()
    {
        if (!IsCompleted)
            throw new ConflictStateException(
                $"TXN {TxnNo} သည် terminal state ({Status}) ဖြစ်နေပြီး ပြောင်းလို့မရပါ။");
    }
}
