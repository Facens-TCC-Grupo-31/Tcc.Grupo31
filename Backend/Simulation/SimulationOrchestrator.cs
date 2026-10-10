namespace Simulation;

public sealed class SimulationOrchestrator
{
    private readonly CollectionExecutor _collectionExecutor = new();

    public async Task<SimulationRunResult> RunAsync(
        SimulationScenarioDefinition definition,
        ISimulationRouteProvider routeProvider,
        ISimulationReadingSink? readingSink = null,
        CancellationToken ct = default)
    {
        ValidateDefinition(definition);
        ArgumentNullException.ThrowIfNull(routeProvider);

        var sensorStates = definition.Sensors
            .Select(sensor => new SimulationSensorState(sensor))
            .ToList();

        var overflowTracker = new SimulationOverflowTracker();
        var ticks = new List<SimulationTickResult>(definition.Timeline.TickCount);

        var startTime = definition.Timeline.StartTimeUtc ?? DateTime.UtcNow;
        var duration = TimeSpan.FromTicks(definition.Timeline.TickInterval.Ticks * definition.Timeline.TickCount);
        var horizon = startTime + duration;

        for (var tickIndex = 0; tickIndex < definition.Timeline.TickCount; tickIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var elapsedTime = TimeSpan.FromTicks(definition.Timeline.TickInterval.Ticks * tickIndex);
            var timestamp = startTime + elapsedTime;

            if (tickIndex > 0)
            {
                foreach (SimulationSensorState sensor in sensorStates)
                {
                    sensor.CurrentFillLevel = Math.Clamp(
                        sensor.CurrentFillLevel + sensor.FillRatePerTick,
                        0,
                        1);
                    sensor.CurrentVolumeLiters = sensor.CurrentFillLevel * sensor.CapacityLiters;
                }
            }

            var readings = sensorStates
                .Select(sensor => new SimulationSensorReading(
                    sensor.SensorId,
                    sensor.CurrentFillLevel,
                    timestamp))
                .ToList();

            var overflowingSensorIds = readings
                .Where(reading => reading.FillLevel >= 1)
                .Select(reading => reading.SensorId)
                .ToArray();

            float averageFillLevel = readings.Count == 0
                ? 0
                : readings.Average(reading => reading.FillLevel);

            var triggerContext = new CollectionTriggerContext(
                timestamp,
                elapsedTime,
                readings.Count,
                averageFillLevel,
                overflowingSensorIds
            );

            if (readingSink is not null)
            {
                await readingSink.PublishAsync(readings, ct);
            }

            bool collectionTriggered = definition.TriggerPolicy.ShouldTrigger(triggerContext);

            CollectionRouteExecutionDecision? routeExecution = null;
            CollectionExecutionResult? collection = null;

            if (collectionTriggered)
            {
                SimulationRoute route = await routeProvider.GetRouteAsync(ct);

                routeExecution = new CollectionRouteExecutionDecision(
                    definition.Route is null
                        ? CollectionRouteExecutionMode.ApplicationStrategy
                        : CollectionRouteExecutionMode.FixedBaselineRoute,
                    route.Coordinates
                );

                collection = _collectionExecutor.Execute(
                    sensorStates,
                    route.SensorIds,
                    route.Coordinates
                );
            }

            overflowTracker.ProcessTick(timestamp, overflowingSensorIds, collection);

            ticks.Add(new(
                    timestamp,
                    readings,
                    triggerContext,
                    collectionTriggered,
                    routeExecution,
                    collection
            ));
        }

        return new SimulationRunResult(ticks, duration, overflowTracker.Complete(horizon), definition);
    }

    private static void ValidateDefinition(SimulationScenarioDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Timeline.TickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Tick interval must be positive.");
        }

        if (definition.Timeline.TickCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Tick count cannot be negative.");
        }

        if (definition.Sensors.Any(sensor => sensor.InitialFillLevel is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Initial fill levels must be between 0 and 1.");
        }

        if (definition.Sensors.Any(sensor => sensor.CapacityLiters <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Sensor capacities in liters must be positive.");
        }

        if (definition.Sensors.Select(sensor => sensor.SensorId).Distinct().Count()
            != definition.Sensors.Count)
        {
            throw new ArgumentException("Sensor IDs must be unique.", nameof(definition));
        }
    }
}