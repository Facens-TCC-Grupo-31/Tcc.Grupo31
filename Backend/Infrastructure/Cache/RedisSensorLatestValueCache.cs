using Application.Cache;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;

namespace Infrastructure.Cache;

internal sealed class RedisSensorLatestValueCache(IConnectionMultiplexer redis) : ISensorLatestValueCache
{
    private const string HashKey = "sensor:latest";
    private IDatabase Db
        => redis.GetDatabase();

    public Task SetAsync(long sensorId, float fillLevel, DateTime timestamp, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { fillLevel, timestamp = timestamp.ToString("O") });
        return Db.HashSetAsync(HashKey, sensorId.ToString(CultureInfo.InvariantCulture), payload);
    }

    public async Task<SensorLatestValue?> GetAsync(long sensorId, CancellationToken ct = default)
    {
        RedisValue raw = await Db.HashGetAsync(HashKey, sensorId.ToString(CultureInfo.InvariantCulture));
        return raw.IsNullOrEmpty ? null : Deserialize(raw!);
    }

    public async Task<IReadOnlyDictionary<long, SensorLatestValue>> GetAllAsync(CancellationToken ct = default)
    {
        HashEntry[] entries = await Db.HashGetAllAsync(HashKey);
        var result = new Dictionary<long, SensorLatestValue>(entries.Length);
        foreach (var entry in entries)
        {
            if (long.TryParse((string?)entry.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                result[id] = Deserialize(entry.Value!);
        }
        return result;
    }

    private static SensorLatestValue Deserialize(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        float fill = doc.RootElement.GetProperty("fillLevel").GetSingle();
        DateTime ts = DateTime.Parse(
            doc.RootElement.GetProperty("timestamp").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return new SensorLatestValue(fill, ts);
    }
}
