using System.Collections.Concurrent;
using MoneyRecord.Domain.Common.Exceptions;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Process-wide keyed mutex for balance-row critical sections — the Mongo port of
/// the original SQL UPDLOCK design (ARCH §10, BR-035). Wait beyond the 5s budget
/// throws <see cref="LockTimeoutException"/> (409 backpressure; clients retry —
/// API-007 §13.1). Correct for the current single-instance deployment; a
/// multi-instance deployment would need a Mongo-based distributed lock.
/// </summary>
public sealed class BalanceLockRegistry
{
    // BR-035: UPDLOCK wait budget (SQL lock_timeout = 5s).
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task AcquireAsync(string key, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(LockTimeout, ct))
            throw new LockTimeoutException();
    }

    public void Release(string key)
    {
        if (_locks.TryGetValue(key, out var sem) && sem.CurrentCount == 0)
            sem.Release();
    }

    // No pruning: a GetOrAdd-vs-prune race could hand two callers different
    // semaphores for the same key (silent double-entry — the exact bug this
    // registry exists to prevent). Entry count = shops + accounts + corrected
    // txns — bounded and small for this deployment.
}
