namespace Application.Cache;

public sealed record SensorLatestValue(float FillLevel, DateTime Timestamp);

public interface ISensorLatestValueCache
{
    Task SetAsync(long sensorId, float fillLevel, DateTime timestamp, CancellationToken ct = default);
    Task<SensorLatestValue?> GetAsync(long sensorId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<long, SensorLatestValue>> GetAllAsync(CancellationToken ct = default);
}
