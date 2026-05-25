using Application.Cache;
using Application.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

internal sealed class SensorLifecycleService(
    AppDbContext db,
    ISensorLatestValueCache latestValueCache,
    ISensorLivenessCache livenessCache,
    IMockSensorRuntimeNotifier? mockSensorRuntimeNotifier = null) : ISensorLifecycleService
{
    public async Task<bool> DeleteAsync(long sensorId, CancellationToken ct = default)
    {
        var sensor = await db.Sensors
            .SingleOrDefaultAsync(item => item.Id == sensorId, ct);

        if (sensor is null)
        {
            return false;
        }

        db.Sensors.Remove(sensor);
        await db.SaveChangesAsync(ct);

        await latestValueCache.RemoveAsync(sensorId, ct);
        await livenessCache.RemoveAsync(sensorId, ct);

        if (mockSensorRuntimeNotifier is not null)
        {
            await mockSensorRuntimeNotifier.StopMockSensorAsync(sensorId, ct);
        }

        return true;
    }
}
