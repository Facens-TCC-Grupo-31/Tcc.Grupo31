using Domain.ValueObjects;

namespace Simulation;

public sealed record SimulationSensorDefinition
{
    public long SensorId { get; init; }
    public float InitialFillLevel { get; init; } = 0;
    public float FillRatePerTick { get; init; }
    public float CapacityLiters { get; init; } = 50;
    public Position? Position { get; init; }
    public int BaselineDistanceMm { get; init; } = 1000;
    public int DesiredReadingMm { get; init; } = 500;

    public SimulationSensorDefinition(
        long sensorId,
        float fillRatePerTick = (1f / 48f),
        Position? position = null)
    {
        SensorId = sensorId;
        FillRatePerTick = fillRatePerTick;
        Position = position;
    }

    public SimulationSensorDefinition(
        long sensorId,
        TimeSpan timeToFull,
        Position? position = null)
    {
        SensorId = sensorId;
        FillRatePerTick = 1f / (float)timeToFull.TotalHours;
        Position = position;
    }
};

public sealed record SimulationSensorReading(
    long SensorId,
    float FillLevel,
    DateTime Timestamp
);

public sealed record SimulationTickResult(
    DateTime Timestamp,
    IReadOnlyList<SimulationSensorReading> Readings,
    CollectionTriggerContext TriggerContext,
    bool CollectionTriggered,
    CollectionRouteExecutionDecision? RouteExecution,
    CollectionExecutionResult? Collection
);

public sealed record SimulationRunResult(
    IReadOnlyList<SimulationTickResult> Ticks,
    TimeSpan SimulationDuration,
    IReadOnlyList<SimulationOverflowEvent> OverflowEvents,
    SimulationScenarioDefinition Definition)
{
    public int CollectionCount => Ticks.Count(tick =>
        tick.CollectionTriggered &&
        tick.Collection is not null &&
        tick.RouteExecution is { RouteCoordinates.Count: > 1 } route &&
        route.RouteCoordinates.Any(position => position != route.RouteCoordinates[0]));

    public float TotalCollectedVolumeLiters => Ticks
        .Where(tick => tick.Collection is not null)
        .Sum(tick => tick.Collection!.CollectedVolumeLiters);

    public double TotalRouteDistance => Ticks
        .Where(tick => tick.Collection is not null)
        .Sum(tick => tick.Collection!.RouteDistanceKilometers);

    public int OverflowEventCount => OverflowEvents.Count(overflowEvent => overflowEvent.Duration > TimeSpan.Zero);

    public TimeSpan TotalOverflowDuration => CalculateUnionDuration(
        OverflowEvents.Select(@event => (@event.StartedAt, @event.EndedAt)));

    public TimeSpan MaximumSingleSensorOverflowDuration => OverflowEvents.Count == 0
        ? TimeSpan.Zero
        : OverflowEvents.Max(@event => @event.Duration);

    private static TimeSpan CalculateUnionDuration(
        IEnumerable<(DateTime StartedAt, DateTime EndedAt)> intervals)
    {
        var ordered = intervals
            .Where(interval => interval.EndedAt > interval.StartedAt)
            .OrderBy(interval => interval.StartedAt)
            .ToList();
        if (ordered.Count == 0)
        {
            return TimeSpan.Zero;
        }

        DateTime currentStart = ordered[0].StartedAt;
        DateTime currentEnd = ordered[0].EndedAt;
        TimeSpan total = TimeSpan.Zero;

        foreach ((DateTime startedAt, DateTime endedAt) in ordered.Skip(1))
        {
            if (startedAt <= currentEnd)
            {
                currentEnd = currentEnd > endedAt ? currentEnd : endedAt;
                continue;
            }

            total += currentEnd - currentStart;
            currentStart = startedAt;
            currentEnd = endedAt;
        }

        return total + (currentEnd - currentStart);
    }
}

public sealed class SimulationSensorState(SimulationSensorDefinition definition)
{
    public long SensorId { get; } = definition.SensorId;
    public float FillRatePerTick { get; } = definition.FillRatePerTick;
    public float CapacityLiters { get; } = definition.CapacityLiters;
    public float CurrentFillLevel { get; set; } = definition.InitialFillLevel;
    public float CurrentVolumeLiters { get; set; } = definition.InitialFillLevel * definition.CapacityLiters;
}
