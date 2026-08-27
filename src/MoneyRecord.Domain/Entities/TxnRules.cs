namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Transaction engine constants (BR-030/035, DD-08; API-007 TXN-001).
/// AppSettings-backed values land with the settings module (SET-*).
/// </summary>
public static class TxnRules
{
    /// <summary>DD-08 amount cap per txn (whole kyats). v1 constant until SET-* module.</summary>
    public const long MaxAmount = 10_000_000;

    /// <summary>BR-030 duplicate soft-warning window (minutes, non-blocking hint).</summary>
    public const int DuplicateWarningWindowMinutes = 5;

    /// <summary>Idempotency replay window (API-007 §1.4 — 24h).</summary>
    public const int IdempotencyRetentionHours = 24;

    /// <summary>BR-013: fee override reason min length.</summary>
    public const int FeeOverrideReasonMinLength = 5;
}
