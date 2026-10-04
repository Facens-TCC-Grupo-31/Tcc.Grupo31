using Application.Cache;

namespace Simulation;

public sealed class InMemoryReadingSink(
    ISensorLatestValueCache cache,
    IReadOnlyDictionary<long, long> logicalToApplicationSensorIds) : ISimulationReadingSink
{
    public async Task PublishAsync(
        IReadOnlyList<SimulationSensorReading> readings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(readings);

        foreach (SimulationSensorReading reading in readings)
        {
            if (!logicalToApplicationSensorIds.TryGetValue(reading.SensorId, out long applicationSensorId))
            {
                throw new InvalidOperationException(
                    $"Simulation reading references unknown sensor ID {reading.SensorId}.");
            }

            await cache.SetAsync(applicationSensorId, reading.FillLevel, reading.Timestamp, ct);
        }
    }
}
