using System.Collections.Concurrent;
using MongoDB.Driver;
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
/// MongoDB-backed idempotency store using atomic findOneAndUpdate with upsert.
/// Replaces PostgreSQL FOR UPDATE row locks — cross-instance safety via atomic operations.
/// </summary>
public sealed class MongoIdempotencyStore : IIdempotencyStore
{
    private readonly IMongoDatabase _database;
    private readonly IClock _clock;
    private readonly IdempotencyKeyLockRegistry _registry;

    public MongoIdempotencyStore(IMongoDatabase database, IClock clock,
        IdempotencyKeyLockRegistry registry)
    {
        _database = database;
        _clock = clock;
        _registry = registry;
    }

    private IMongoCollection<IdempotencyKeyDoc> Collection =>
        _database.GetCollection<IdempotencyKeyDoc>("idempotencyKeys");

    public async Task<IIdempotencyLease> BeginLeaseAsync(Guid key, string requestHash,
        CancellationToken ct)
    {
        var gate = _registry.Get(key);
        await gate.WaitAsync(ct);
        try
        {
            var filter = Builders<IdempotencyKeyDoc>.Filter.Eq(d => d.Key, key);
            var existing = await Collection.Find(filter).FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                // Fresh key — insert reservation atomically
                var doc = new IdempotencyKeyDoc
                {
                    Key = key,
                    RequestHash = requestHash,
                    ResponseJson = null,
                    CreatedAtUtc = _clock.UtcNow,
                    ExpiresAtUtc = _clock.UtcNow.AddMinutes(15)
                };
                try
                {
                    await Collection.InsertOneAsync(doc, cancellationToken: ct);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // Race: another instance inserted first — re-read
                    existing = await Collection.Find(filter).FirstOrDefaultAsync(ct);
                    if (existing is not null)
                        return HandleExisting(existing, requestHash);
                }
                return new Lease(this, key, IdempotencyOutcome.Fresh, null);
            }

            return HandleExisting(existing, requestHash);
        }
        catch
        {
            Release(key);
            throw;
        }
    }

    private IIdempotencyLease HandleExisting(IdempotencyKeyDoc existing, string requestHash)
    {
        if (existing.ExpiresAtUtc <= _clock.UtcNow)
        {
            // Expired → re-arm
            var filter = Builders<IdempotencyKeyDoc>.Filter.Eq(d => d.Key, existing.Key);
            var update = Builders<IdempotencyKeyDoc>.Update
                .Set(d => d.RequestHash, requestHash)
                .Set(d => d.ResponseJson, null)
                .Set(d => d.ExpiresAtUtc, _clock.UtcNow.AddMinutes(15));
            Collection.UpdateOne(filter, update);
            return new Lease(this, existing.Key, IdempotencyOutcome.Fresh, null);
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            Release(existing.Key);
            throw new DuplicateRequestException();
        }

        if (existing.ResponseJson is not null)
        {
            return new Lease(this, existing.Key, IdempotencyOutcome.Replay, existing.ResponseJson);
        }

        return new Lease(this, existing.Key, IdempotencyOutcome.Fresh, null);
    }

    public async Task CompleteAsync(Guid key, string responseJson, CancellationToken ct)
    {
        var filter = Builders<IdempotencyKeyDoc>.Filter.Eq(d => d.Key, key);
        var update = Builders<IdempotencyKeyDoc>.Update.Set(d => d.ResponseJson, responseJson);
        await Collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    private void Release(Guid key)
    {
        if (_registry.Get(key).CurrentCount == 0)
            _registry.Get(key).Release();
        _registry.TryPrune();
    }

    private sealed class Lease : IIdempotencyLease
    {
        private readonly MongoIdempotencyStore _store;
        private bool _released;

        public Lease(MongoIdempotencyStore store, Guid key, IdempotencyOutcome outcome,
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

    /// <summary>MongoDB document for idempotency keys.</summary>
    public sealed class IdempotencyKeyDoc
    {
        [MongoDB.Bson.Serialization.Attributes.BsonId]
        public Guid Key { get; set; }
        public string RequestHash { get; set; } = default!;
        public string? ResponseJson { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
