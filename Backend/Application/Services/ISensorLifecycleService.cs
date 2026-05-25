namespace Application.Services;

public interface ISensorLifecycleService
{
    Task<bool> DeleteAsync(long sensorId, CancellationToken ct = default);
}
