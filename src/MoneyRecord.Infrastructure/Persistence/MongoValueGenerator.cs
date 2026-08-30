using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// MongoDB value generator for long primary keys.
/// Uses atomic findOneAndUpdate with $inc on a counters collection.
/// </summary>
public sealed class MongoValueGenerator : ValueGenerator<long>
{
    private readonly Func<IMongoDatabase> _databaseFactory;

    public MongoValueGenerator(Func<IMongoDatabase> databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public override bool GeneratesTemporaryValues => false;

    public override long Next(EntityEntry entry)
    {
        var database = _databaseFactory();
        var entityType = entry.Context.Model.FindEntityType(entry.Entity.GetType());
        var collectionName = entityType?.ShortName() ?? "default";
        var counterName = $"{collectionName}_id";

        var collection = database.GetCollection<BsonDocument>("counters");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", counterName);
        var update = Builders<BsonDocument>.Update.Inc("seq", 1);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        var result = collection.FindOneAndUpdate(filter, update, options);
        return result["seq"].AsInt64;
    }
}
