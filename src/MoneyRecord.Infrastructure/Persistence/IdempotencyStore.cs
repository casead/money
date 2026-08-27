using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Common.Exceptions;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>Singleton registry of per-key async gates (serializes same-key submissions).</summary>
public sealed class IdempotencyKeyLockRegistry
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public SemaphoreSlim Get(Guid key) => _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    /// <summary>Bounded cleanup: drop gates with no waiters once in a while.</summary>
    public void TryPrune()
    {
        if (_locks.Count < 4096) return;
        foreach (var kv in _locks.Where(kv => kv.Value.CurrentCount == 1).Take(1024))
            _locks.TryRemove(kv.Key, out _);
    }
}

/// <summary>
/// UPDLOCK + per-key gate idempotency store (BR-031 / TC-600d storm semantics).
/// The in-process gate serializes identical-key submissions; the DB-side
/// SELECT … WITH (UPDLOCK) inside the ambient transaction covers cross-instance
/// deployment. Losers observe the winner's committed receipt and replay it.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly MoneyRecordDbContext _db;
    private readonly IClock _clock;
    private readonly IdempotencyKeyLockRegistry _registry;

    public IdempotencyStore(MoneyRecordDbContext db, IClock clock,
        IdempotencyKeyLockRegistry registry)
    {
        _db = db;
        _clock = clock;
        _registry = registry;
    }

    public async Task<IIdempotencyLease> BeginLeaseAsync(Guid key, string requestHash,
        CancellationToken ct)
    {
        var gate = _registry.Get(key);
        await gate.WaitAsync(ct);
        try
        {
            // Lock the key row if it exists (row lock held until commit — cross-instance).
            var locked = await _db.Database
                .SqlQuery<ReservedRow>($@"
                    SELECT ""Id"" AS ""Id"", ""RequestHash"" AS ""RequestHash"",
                           ""ResponseJson"" AS ""ResponseJson"", ""ExpiresAtUtc"" AS ""ExpiresAtUtc""
                    FROM ""IdempotencyKeys""
                    WHERE ""Key"" = {key}
                    FOR UPDATE")
                .ToListAsync(ct);

            if (locked.Count == 0)
            {
                // Guard against a stale tracked instance from an earlier failed attempt
                // in the same scope (its insert rolled back but tracker kept the entity).
                var stale = _db.IdempotencyKeys.Local.FirstOrDefault(k => k.Key == key);
                if (stale is not null)
                {
                    stale.ReReserve(requestHash, _clock.UtcNow);
                }
                else
                {
                    _db.IdempotencyKeys.Add(IdempotencyKey.Reserve(key, requestHash, _clock.UtcNow));
                }
                return new Lease(this, key, IdempotencyOutcome.Fresh, null);
            }

            var row = locked[0];

            if (row.ExpiresAtUtc <= _clock.UtcNow)
            {
                // Expired → re-arm the SAME tracked row in place.
                var expired = await _db.IdempotencyKeys.FirstAsync(k => k.Id == row.Id, ct);
                expired.ReReserve(requestHash, _clock.UtcNow);
                return new Lease(this, key, IdempotencyOutcome.Fresh, null);
            }

            if (!string.Equals(row.RequestHash, requestHash, StringComparison.Ordinal))
            {
                // Abort without a lease → exactly one release here (the catch-free path).
                Release(key);
                throw new DuplicateRequestException();
            }

            if (row.ResponseJson is not null)
            {
                // Replay: KEEP holding the gate — the caller's lease disposal performs
                // the single release. Releasing here AND on dispose would double-count
                // under contention and let two submissions run concurrently (23505).
                return new Lease(this, key, IdempotencyOutcome.Replay, row.ResponseJson);
            }

            // Reserved but unfinished (previous attempt rolled back) → fresh retry.
            return new Lease(this, key, IdempotencyOutcome.Fresh, null);
        }
        catch
        {
            Release(key);
            throw;
        }
    }

    public async Task CompleteAsync(Guid key, string responseJson, CancellationToken ct)
    {
        // Prefer the tracked instance inserted by ReserveAsync in this same
        // transaction — a DB re-query here races against insert visibility.
        var entity = _db.IdempotencyKeys.Local.FirstOrDefault(k => k.Key == key)
            ?? await _db.IdempotencyKeys.FirstOrDefaultAsync(k => k.Key == key, ct)
            ?? throw new InvalidOperationException(
                $"Idempotency key {key} was never reserved — CompleteAsync called without ReserveAsync.");
        entity.Complete(responseJson);
        await _db.SaveChangesAsync(ct);
    }

    private void Release(Guid key)
    {
        if (_registry.Get(key).CurrentCount == 0)
            _registry.Get(key).Release();
        _registry.TryPrune();
    }

    private sealed class Lease : IIdempotencyLease
    {
        private readonly IdempotencyStore _store;
        private bool _released;

        public Lease(IdempotencyStore store, Guid key, IdempotencyOutcome outcome,
            string? responseJson)
        {
            _store = store;
            Key = key;
            Outcome = outcome;
            ResponseJson = responseJson;
        }

        public Guid Key { get; }
        public IdempotencyOutcome Outcome { get; }
        public string? ResponseJson { get; }

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                _store.Release(Key);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReservedRow
    {
        public long Id { get; set; }
        public string RequestHash { get; set; } = default!;
        public string? ResponseJson { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
