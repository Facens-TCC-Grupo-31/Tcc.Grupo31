namespace Application.Cache;

public interface ISensorLivenessCache
{
    Task RefreshAsync(long sensorId, CancellationToken ct = default);
    Task<bool> IsAliveAsync(long sensorId, CancellationToken ct = default);
    Task RemoveAsync(long sensorId, CancellationToken ct = default);
}