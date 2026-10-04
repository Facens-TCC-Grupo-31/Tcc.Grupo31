namespace Simulation;

public sealed record SimulationOverflowEvent(
    long SensorId,
    DateTime StartedAt,
    DateTime EndedAt)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

public sealed class SimulationOverflowTracker
{
    private readonly Dictionary<long, DateTime> _activeStarts = [];
    private readonly List<SimulationOverflowEvent> _events = [];

    public void ProcessTick(
        DateTime timestamp,
        IReadOnlyList<long> criticalSensorIds,
        CollectionExecutionResult? collection)
    {
        var criticalIds = criticalSensorIds.ToHashSet();

        foreach (long sensorId in _activeStarts.Keys.Where(id => !criticalIds.Contains(id)).ToArray())
        {
            Close(sensorId, timestamp);
        }

        foreach (long sensorId in criticalIds)
        {
            _activeStarts.TryAdd(sensorId, timestamp);
        }

        if (collection is not null)
        {
            foreach (long sensorId in collection.ServedSensorIds)
            {
                Close(sensorId, timestamp);
            }
        }
    }

    public IReadOnlyList<SimulationOverflowEvent> Complete(DateTime horizon)
    {
        foreach (long sensorId in _activeStarts.Keys.ToArray())
        {
            Close(sensorId, horizon);
        }

        return _events.ToArray();
    }

    private void Close(long sensorId, DateTime endedAt)
    {
        if (!_activeStarts.Remove(sensorId, out DateTime startedAt))
        {
            return;
        }

        _events.Add(new SimulationOverflowEvent(sensorId, startedAt, endedAt));
    }
}