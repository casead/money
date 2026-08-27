namespace MoneyRecord.Domain.Entities;

/// <summary>
/// Idempotency replay store (API-007 §1.4, BR-031). Key + request-hash + response
/// snapshot with a 24h window. Same key + same payload → replay; different payload
/// → 409 DUPLICATE_REQUEST. UQ(Key) is the race backstop.
/// </summary>
public class IdempotencyKey
{
    public long Id { get; private set; }

    public Guid Key { get; private set; }

    /// <summary>SHA-256 hex of the canonical request body.</summary>
    public string RequestHash { get; private set; } = default!;

    /// <summary>Serialized receipt payload — filled when the txn commits.</summary>
    public string? ResponseJson { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    private IdempotencyKey() { } // EF Core

    public static IdempotencyKey Reserve(Guid key, string requestHash, DateTime nowUtc) =>
        new()
        {
            Key = key,
            RequestHash = requestHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(TxnRules.IdempotencyRetentionHours)
        };

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;

    public bool Matches(string requestHash) =>
        string.Equals(RequestHash, requestHash, StringComparison.Ordinal);

    /// <summary>Re-arms an expired/unfinished reservation for a fresh submission window.</summary>
    public void ReReserve(string requestHash, DateTime nowUtc)
    {
        RequestHash = requestHash;
        ResponseJson = null;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.AddHours(TxnRules.IdempotencyRetentionHours);
    }

    public void Complete(string responseJson) => ResponseJson = responseJson;
}
