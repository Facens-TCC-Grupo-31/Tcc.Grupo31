namespace Application.Services;

public interface IMockSensorRuntimeNotifier
{
    Task StartMockSensorAsync(
        long sensorId,
        int baselineDistanceMm,
        int desiredReadingMm,
        CancellationToken ct = default);

    Task StopMockSensorAsync(long sensorId, CancellationToken ct = default);
}