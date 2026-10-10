using Domain.ValueObjects;

namespace Simulation;

public sealed record SimulationGraphDefinition(string OsmPath);

public sealed record SimulationRouteDefinition(
    IReadOnlyList<Position> Coordinates,
    IReadOnlyList<long> SensorIds
);

public record SimulationScenarioTimelineDefinition
{
    public TimeSpan TickInterval { get; init; }
    public int TickCount { get; init; }
    public DateTime? StartTimeUtc { get; init; }

    public SimulationScenarioTimelineDefinition(TimeSpan SimulationTimespan, TimeSpan TickInterval)
    {
        if (SimulationTimespan <= TimeSpan.Zero)
            throw new ArgumentException("Simulation timespan must be greater than zero.", nameof(SimulationTimespan));

        if (TickInterval <= TimeSpan.Zero)
            throw new ArgumentException("Tick interval must be greater than zero.", nameof(TickInterval));
        
        if (SimulationTimespan < TickInterval)
            throw new ArgumentException("Simulation timespan must be greater than tick interval.", nameof(SimulationTimespan));

        if (SimulationTimespan.Ticks % TickInterval.Ticks != 0)
            throw new ArgumentException("Simulation timespan must be a multiple of tick interval.", nameof(SimulationTimespan));

        this.TickCount = (int)(SimulationTimespan.Ticks / TickInterval.Ticks);
        this.TickInterval = TickInterval;
    }

    public SimulationScenarioTimelineDefinition(int TickCount, TimeSpan TickInterval)
    {
        if (TickCount <= 0)
            throw new ArgumentException("Tick count must be greater than zero.", nameof(TickCount));
        if (TickInterval <= TimeSpan.Zero)
            throw new ArgumentException("Tick interval must be greater than zero.", nameof(TickInterval));

        this.TickCount = TickCount;
        this.TickInterval = TickInterval;
    }
};

public sealed record SimulationScenarioDefinition(
    SimulationGraphDefinition Graph,
    SimulationScenarioTimelineDefinition Timeline,
    ICollectionTriggerPolicy TriggerPolicy,
    IReadOnlyList<SimulationSensorDefinition> Sensors,
    SimulationRouteDefinition? Route = null
);