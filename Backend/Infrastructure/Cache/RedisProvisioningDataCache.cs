using Application.Cache;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure.Cache;

internal sealed class RedisProvisioningDataCache(IConnectionMultiplexer redis) : IProvisioningDataCache
{
    private static TimeSpan Ttl
        => TimeSpan.FromMinutes(5);

    private IDatabase Db
        => redis.GetDatabase();

    private static RedisKey Key(long sensorId)
        => $"reg:{sensorId}";

    public Task SetAsync(long sensorId, ProvisioningRegistrationContext context, CancellationToken ct = default)
    {
        string payload = JsonSerializer.Serialize(context);
        return Db.StringSetAsync(Key(sensorId), payload, Ttl);
    }

    public async Task<ProvisioningRegistrationContext?> ConsumeAsync(long sensorId, CancellationToken ct = default)
    {
        RedisValue result = await Db.StringGetDeleteAsync(Key(sensorId));
        if (result.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProvisioningRegistrationContext>((string)result!);
    }
}
