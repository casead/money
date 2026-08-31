using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MoneyRecord.Infrastructure.Persistence;

/// <summary>
/// Registers custom BSON serializers for types not natively supported by MongoDB driver.
/// Must be called once at application startup before any MongoDB operations.
/// </summary>
public static class MongoSerializers
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        BsonSerializer.RegisterSerializer(new DateOnlySerializer());
        _registered = true;
    }
}

/// <summary>
/// Serializes DateOnly as a DateTime (midnight UTC) for MongoDB storage.
/// </summary>
public sealed class DateOnlySerializer : SerializerBase<DateOnly>
{
    public override DateOnly Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        return bsonType switch
        {
            BsonType.DateTime => DateOnly.FromDateTime(BsonDateTime.Create(context.Reader.ReadDateTime()).ToUniversalTime()),
            BsonType.Int32 => DateOnly.FromDayNumber(context.Reader.ReadInt32()),
            BsonType.Int64 => DateOnly.FromDayNumber((int)context.Reader.ReadInt64()),
            BsonType.String => DateOnly.Parse(context.Reader.ReadString()),
            _ => throw new BsonSerializationException($"Cannot deserialize DateOnly from BSON type {bsonType}.")
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateOnly value)
    {
        var utcDateTime = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
        context.Writer.WriteDateTime(utcDateTime.Ticks);
    }
}
