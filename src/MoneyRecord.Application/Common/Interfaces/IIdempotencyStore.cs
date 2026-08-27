namespace MoneyRecord.Application.Common.Interfaces;

public enum IdempotencyOutcome
{
    /// <summary>No usable reservation — proceed with the business flow.</summary>
    Fresh,

    /// <summary>Completed reservation with identical payload — replay ResponseJson.</summary>
    Replay
}

public sealed record IdempotencyCheckResult(IdempotencyOutcome Outcome, string? ResponseJson);

/// <summary>Held for the duration of one logical submission; releases the per-key gate.</summary>
public interface IIdempotencyLease : IAsyncDisposable
{
    Guid Key { get; }
    IdempotencyOutcome Outcome { get; }

    /// <summary>Serialized original receipt when Outcome == Replay.</summary>
    string? ResponseJson { get; }
}

/// <summary>
/// Idempotency reservation port (BR-031, API-007 §1.4).
/// BeginLeaseAsync MUST serialize concurrent same-key submissions (in-process gate +
/// DB-row lock for cross-instance safety): the loser waits for the winner's commit and
/// then replays the stored response (TC-600d storm semantics).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Acquires the per-key lease and checks/creates the reservation under lock.
    /// Throws DuplicateRequestException when an existing completed reservation has a
    /// different payload hash. Expired reservations are replaced transparently.
    /// </summary>
    Task<IIdempotencyLease> BeginLeaseAsync(Guid key, string requestHash,
        CancellationToken ct);

    /// <summary>Stores the serialized receipt on the (leased) reservation and saves.</summary>
    Task CompleteAsync(Guid key, string responseJson, CancellationToken ct);
}
