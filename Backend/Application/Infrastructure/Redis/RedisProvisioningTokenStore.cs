using Application.Infrastructure.Abstractions;
using StackExchange.Redis;

namespace Application.Infrastructure.Redis;

internal sealed class RedisProvisioningTokenStore(IConnectionMultiplexer redis) : IProvisioningTokenStore
{
    private static TimeSpan Ttl
        => TimeSpan.FromMinutes(5);

    private IDatabase Db
        => redis.GetDatabase();

    private static RedisKey Key(long sensorId)
        => $"reg:{sensorId}";

    public Task SetAsync(long sensorId, string token, CancellationToken ct = default)
        => Db.StringSetAsync(Key(sensorId), token, Ttl);

    public async Task<string?> ConsumeAsync(long sensorId, CancellationToken ct = default)
    {
        RedisValue result = await Db.StringGetDeleteAsync(Key(sensorId));
        return result.IsNullOrEmpty ? null : (string?)result;
    }
}
