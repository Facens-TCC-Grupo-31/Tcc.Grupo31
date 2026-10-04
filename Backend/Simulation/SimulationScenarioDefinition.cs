using Domain.ValueObjects;

namespace Simulation;

public sealed record SimulationGraphDefinition(string OsmPath);

public sealed record SimulationRouteDefinition(
    IReadOnlyList<Position> Coordinates,
    IReadOnlyList<long> SensorIds
);

public sealed record SimulationScenarioDefinition(
    SimulationGraphDefinition Graph,
    TimeSpan TickInterval,
    int TickCount,
    ICollectionTriggerPolicy TriggerPolicy,
    IReadOnlyList<SimulationSensorDefinition> Sensors,
    SimulationRouteDefinition? Route = null
);