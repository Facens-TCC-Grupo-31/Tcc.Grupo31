using Application.Cache;
using Infrastructure.Services.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Cache;

internal sealed class RedisSensorLivenessCache(
    IConnectionMultiplexer redis,
    IOptions<SensorLivenessOptions> options) : ISensorLivenessCache
{
    private readonly SensorLivenessOptions _options = options.Value;

    private IDatabase Db
        => redis.GetDatabase();

    private static RedisKey Key(long sensorId)
        => $"sensor:{sensorId}:alive";

    public Task RefreshAsync(long sensorId, CancellationToken ct = default)
        => Db.StringSetAsync(Key(sensorId), "1", _options.HeartbeatTtl);

    public Task<bool> IsAliveAsync(long sensorId, CancellationToken ct = default)
        => Db.KeyExistsAsync(Key(sensorId));

    public Task RemoveAsync(long sensorId, CancellationToken ct = default)
        => Db.KeyDeleteAsync(Key(sensorId));
}