using MongoDB.Driver;
using MoneyRecord.Application.Common.Interfaces;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// MongoDB-backed transaction number generator using atomic findOneAndUpdate with $inc.
/// Replaces PostgreSQL SEQUENCE — race-free, no lock contention.
/// </summary>
public sealed class MongoTxnNumberGenerator : ITxnNumberGenerator
{
    private readonly IMongoDatabase _database;

    public MongoTxnNumberGenerator(IMongoDatabase database) => _database = database;

    public async Task<long> NextAsync(CancellationToken ct)
    {
        var counters = _database.GetCollection<CounterDocument>("counters");
        var filter = Builders<CounterDocument>.Filter.Eq(c => c.Id, "txnNo");
        var update = Builders<CounterDocument>.Update.Inc(c => c.Seq, 1);
        var options = new FindOneAndUpdateOptions<CounterDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        var result = await counters.FindOneAndUpdateAsync(filter, update, options, ct);
        return result.Seq;
    }

    /// <summary>
    /// MongoDB counter document for generating sequential IDs.
    /// </summary>
    public sealed class CounterDocument
    {
        [MongoDB.Bson.Serialization.Attributes.BsonId]
        public string Id { get; set; } = default!;
        public long Seq { get; set; }
    }
}
