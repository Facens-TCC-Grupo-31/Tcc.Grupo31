using Application.Common.Dtos;
using Application.Services;
using Domain.ValueObjects;
using System.Text.Json;
using Xunit;

namespace Simulation.Tests;

public sealed class SimulationPolicyTests
{
    [Fact]
    public void CommandLineOptions_DefaultsToDeterministic()
    {
        SimulationCommandLineOptions options = SimulationCommandLineOptions.Parse([]);

        Assert.Equal(SimulationScenarioKind.Deterministic, options.Scenario);
    }

    [Theory]
    [InlineData("deterministic", SimulationScenarioKind.Deterministic)]
    [InlineData("e2e-fixed", SimulationScenarioKind.E2eFixed)]
    [InlineData("e2e-dynamic", SimulationScenarioKind.E2eDynamic)]
    public void CommandLineOptions_ParsesScenario(string value, SimulationScenarioKind expected)
    {
        SimulationCommandLineOptions options = SimulationCommandLineOptions.Parse(["--scenario", value]);

        Assert.Equal(expected, options.Scenario);
    }

    [Theory]
    [InlineData("--e2e")]
    [InlineData("--dynamic")]
    [InlineData("--compose-file")]
    public void CommandLineOptions_RejectsRemovedFeatureOptions(string argument)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SimulationCommandLineOptions.Parse([argument]));

        Assert.Contains(SimulationCommandLineOptions.Usage, exception.Message);
    }

    [Fact]
    public void CommandLineOptions_RejectsUnknownScenario()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SimulationCommandLineOptions.Parse(["--scenario", "unknown"]));

        Assert.Contains("Unknown scenario", exception.Message);
        Assert.Contains(SimulationCommandLineOptions.Usage, exception.Message);
    }

    [Fact]
    public void CommandLineOptions_RejectsMissingScenarioValue()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SimulationCommandLineOptions.Parse(["--scenario"]));

        Assert.Contains("requires a value", exception.Message);
    }

    [Fact]
    public void FixedBaselineRouteExecution_UsesBaselineMode()
    {
        var routeStrategy = new FixedBaselineCollectionRouteExecutionStrategy(
            new Position(-23.5505, -46.6333),
            [new Position(-23.5515, -46.6340), new Position(-23.5495, -46.6320)]);

        CollectionRouteExecutionDecision decision = routeStrategy.SelectExecution(
            new CollectionRouteRequestOptionsDto());

        Assert.Equal(CollectionRouteExecutionMode.FixedBaselineRoute, decision.Mode);
        Assert.NotEmpty(decision.RouteCoordinates);
    }

    [Fact]
    public void DynamicThresholdPolicy_Triggers_WhenAverageFillCrossesThreshold()
    {
        var policy = new AverageFillThresholdCollectionTriggerPolicy(0.75f);

        bool shouldTrigger = policy.ShouldTrigger(new CollectionTriggerContext(
            DateTime.UtcNow,
            ActiveSensorCount: 4,
            AverageFillLevel: 0.80f,
            CriticalSensorIds: Array.Empty<long>()));

        Assert.True(shouldTrigger);
    }

    [Fact]
    public void HybridPolicy_Triggers_WhenAnySensorHitsCriticalThreshold()
    {
        var policy = new HybridCollectionTriggerPolicy(
            new AverageFillThresholdCollectionTriggerPolicy(0.70f),
            new CriticalThresholdCollectionTriggerPolicy(0.90f));

        bool shouldTrigger = policy.ShouldTrigger(new CollectionTriggerContext(
            DateTime.UtcNow,
            ActiveSensorCount: 3,
            AverageFillLevel: 0.55f,
            CriticalSensorIds: [42L]));

        Assert.True(shouldTrigger);
    }

    [Fact]
    public void Runner_EvolvesSensors_AndSelectsRouteOnlyAfterTrigger()
    {
        var routeStrategy = new FixedBaselineCollectionRouteExecutionStrategy(
            new Position(-23.5505, -46.6333),
            [new Position(-23.5515, -46.6340)]);
        var scenario = new SimulationScenario(
            TimeSpan.FromMinutes(1),
            TickCount: 4,
            Sensors: [new SimulationSensorDefinition(7L, 0.20f, 0.30f)],
            TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(0.75f),
            RouteExecutionStrategy: routeStrategy,
            RouteRequestOptions: new CollectionRouteRequestOptionsDto(),
            RouteSensorIds: [7L]);

        SimulationRunResult result = new SimulationRunner().Run(
            scenario,
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(4, result.Ticks.Count);
        Assert.Equal(1, result.CollectionCount);
        Assert.False(result.Ticks[0].CollectionTriggered);
        Assert.Null(result.Ticks[0].RouteExecution);
        Assert.Equal(0.80f, result.Ticks[2].Readings[0].FillLevel, precision: 3);
        Assert.Equal(CollectionRouteExecutionMode.FixedBaselineRoute, result.Ticks[2].RouteExecution!.Mode);
        Assert.Equal(0.80f, result.Ticks[2].Collection!.CollectedVolumeLiters, precision: 3);
        Assert.Equal(0.30f, result.Ticks[3].Readings[0].FillLevel, precision: 3);
    }

    [Fact]
    public void Runner_ClampsFillLevelAtOne()
    {
        var scenario = new SimulationScenario(
            TimeSpan.FromSeconds(1),
            TickCount: 2,
            Sensors: [new SimulationSensorDefinition(7L, 0.90f, 0.30f)],
            TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(1.0f),
            RouteExecutionStrategy: new FixedBaselineCollectionRouteExecutionStrategy(
                new Position(-23.5505, -46.6333),
                []),
            RouteRequestOptions: new CollectionRouteRequestOptionsDto(),
            RouteSensorIds: [7L]);

        SimulationRunResult result = new SimulationRunner().Run(scenario, DateTime.UtcNow);

        Assert.Equal(1.0f, result.Ticks[1].Readings[0].FillLevel);
    }

    [Fact]
    public void RunResult_ReportsRouteAndCriticalSensorAggregates()
    {
        var routeStrategy = new FixedBaselineCollectionRouteExecutionStrategy(
            new Position(-23.5505, -46.6333),
            [
                new Position(-23.5505, -46.6333),
                new Position(-23.5515, -46.6340)
            ]);
        var scenario = new SimulationScenario(
            TimeSpan.FromMinutes(1),
            TickCount: 1,
            Sensors: [new SimulationSensorDefinition(7L, 0.90f, 0.01f, 0.75f, 10f)],
            TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(0.75f),
            RouteExecutionStrategy: routeStrategy,
            RouteRequestOptions: new CollectionRouteRequestOptionsDto(),
            RouteSensorIds: [7L]);

        SimulationRunResult result = new SimulationRunner().Run(
            scenario,
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, result.CollectionCount);
        Assert.Equal(9f, result.TotalCollectedVolumeLiters, precision: 3);
        Assert.True(result.TotalRouteDistance > 0);
        Assert.Equal(TimeSpan.FromMinutes(1), result.SimulationDuration);
        Assert.Equal(1, result.OverflowEventCount);
        Assert.Equal(TimeSpan.Zero, result.TotalOverflowDuration);
        Assert.Equal(TimeSpan.Zero, result.MaximumSingleSensorOverflowDuration);
    }

    [Fact]
    public void OverflowTracker_UsesUnionDuration_AndSingleSensorMaximum()
    {
        var tracker = new SimulationOverflowTracker();
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        tracker.ProcessTick(start, [1L], null);
        tracker.ProcessTick(start.AddMinutes(1), [1L, 2L], null);
        tracker.ProcessTick(start.AddMinutes(2), [2L], null);

        IReadOnlyList<SimulationOverflowEvent> events = tracker.Complete(start.AddMinutes(3));

        Assert.Equal(2, events.Count);
        Assert.Equal(TimeSpan.FromMinutes(2), events.Single(@event => @event.SensorId == 1L).Duration);
        Assert.Equal(TimeSpan.FromMinutes(2), events.Single(@event => @event.SensorId == 2L).Duration);
        Assert.Equal(
            TimeSpan.FromMinutes(3),
            CalculateUnionDuration(events));
    }

    [Fact]
    public void OverflowTracker_ClosesEpisodeAtCollection_AndAllowsAnotherCrossing()
    {
        var tracker = new SimulationOverflowTracker();
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var collection = new CollectionExecutionResult([1L], 1f, 0);

        tracker.ProcessTick(start, [1L], null);
        tracker.ProcessTick(start.AddMinutes(1), [1L], collection);
        tracker.ProcessTick(start.AddMinutes(2), [], null);
        tracker.ProcessTick(start.AddMinutes(3), [1L], null);

        IReadOnlyList<SimulationOverflowEvent> events = tracker.Complete(start.AddMinutes(4));

        Assert.Equal(2, events.Count);
        Assert.Equal(TimeSpan.FromMinutes(1), events[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(1), events[1].Duration);
    }

    private static TimeSpan CalculateUnionDuration(
        IReadOnlyList<SimulationOverflowEvent> events)
    {
        DateTime start = events.Min(@event => @event.StartedAt);
        DateTime end = events.Max(@event => @event.EndedAt);
        return end - start;
    }

    [Fact]
    public void Runner_ResetsOnlyServedSensors_AndKeepsTickInterval()
    {
        var scenario = new SimulationScenario(
            TimeSpan.FromMinutes(1),
            TickCount: 3,
            Sensors:
            [
                new SimulationSensorDefinition(7L, 0.80f, 0.05f, CapacityLiters: 10f),
                new SimulationSensorDefinition(8L, 0.80f, 0.05f, CapacityLiters: 20f)
            ],
            TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(0.75f),
            RouteExecutionStrategy: new FixedBaselineCollectionRouteExecutionStrategy(
                new Position(-23.5505, -46.6333),
                [new Position(-23.5515, -46.6340)]),
            RouteRequestOptions: new CollectionRouteRequestOptionsDto(),
            RouteSensorIds: [7L]);

        DateTime startTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        SimulationRunResult result = new SimulationRunner().Run(scenario, startTime);
        CollectionExecutionResult collection = result.Ticks[0].Collection
            ?? throw new InvalidOperationException("Expected a collection on the first tick.");

        Assert.Equal(startTime.AddMinutes(1), result.Ticks[1].Timestamp);
        Assert.Equal([7L], collection.ServedSensorIds);
        Assert.Equal(8f, collection.CollectedVolumeLiters, precision: 3);
        Assert.Equal(0.05f, result.Ticks[1].Readings.Single(reading => reading.SensorId == 7L).FillLevel, precision: 3);
        Assert.Equal(0.85f, result.Ticks[1].Readings.Single(reading => reading.SensorId == 8L).FillLevel, precision: 3);
    }

    [Fact]
    public void ScenarioFactory_CreatesScenarioFromClassDefinition()
    {
        var definition = new SimulationScenarioDefinition(
            new SimulationGraphDefinition("fixtures/scenario.osm"),
            TimeSpan.FromSeconds(30),
            TickCount: 2,
            Sensors: [new SimulationSensorDefinition(7L, 0.40f, 0.10f, CapacityLiters: 5f)],
            AverageFillThreshold: 0.60f,
            CriticalFillThreshold: 0.90f,
            Route: new SimulationRouteDefinition(
                [new Position(-23.55, -46.63), new Position(-23.56, -46.64)],
                [7L]));

        SimulationScenario scenario = SimulationScenarioFactory.Create(definition);

        Assert.Equal(TimeSpan.FromSeconds(30), scenario.TickInterval);
        Assert.Equal(2, scenario.TickCount);
        Assert.Equal(5f, scenario.Sensors[0].CapacityLiters);
        Assert.Equal([7L], scenario.RouteSensorIds);
    }

    [Fact]
    public async Task ReportWriter_WritesStateRouteAndCollectionVolume()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"simulation-report-{Guid.NewGuid():N}.json");
        var routeStrategy = new FixedBaselineCollectionRouteExecutionStrategy(
            new Position(-23.5505, -46.6333),
            [new Position(-23.5505, -46.6333), new Position(-23.5515, -46.6340)]);
        var scenario = new SimulationScenario(
            TimeSpan.FromMinutes(1),
            TickCount: 1,
            Sensors: [new SimulationSensorDefinition(7L, 0.80f, 0.05f, CapacityLiters: 120f)],
            TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(0.75f),
            RouteExecutionStrategy: routeStrategy,
            RouteRequestOptions: new CollectionRouteRequestOptionsDto(),
            RouteSensorIds: [7L]);
        SimulationRunResult result = new SimulationRunner().Run(scenario, DateTime.UtcNow);

        try
        {
            await SimulationReportWriter.WriteAsync(filePath, result, "deterministic");

            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
            JsonElement root = document.RootElement;
            JsonElement firstTick = root.GetProperty("ticks")[0];

            Assert.Equal("deterministic", root.GetProperty("scenarioName").GetString());
            Assert.Equal(96f, root.GetProperty("summary").GetProperty("totalCollectedVolumeLiters").GetSingle(), precision: 3);
            Assert.Equal(7L, firstTick.GetProperty("state").GetProperty("readings")[0].GetProperty("sensorId").GetInt64());
            Assert.Equal("FixedBaselineRoute", firstTick.GetProperty("route").GetProperty("mode").GetString());
            Assert.Equal(96f, firstTick.GetProperty("collection").GetProperty("collectedVolumeLiters").GetSingle(), precision: 3);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task RouteProviderFactory_PrefersFixedRoute_WithoutCallingApplication()
    {
        var definition = new SimulationScenarioDefinition(
            new SimulationGraphDefinition("scenario.osm"),
            TimeSpan.FromMinutes(1),
            TickCount: 1,
            Sensors: [new SimulationSensorDefinition(7L, 0.4f, 0.1f)],
            AverageFillThreshold: 0.75f,
            CriticalFillThreshold: 0.9f,
            Route: new SimulationRouteDefinition(
                [new Position(-23.55, -46.63)],
                [7L]));

        ISimulationRouteProvider provider = SimulationRouteProviderFactory.Create(
            definition,
            new ThrowingCollectionRoutingService());

        SimulationRoute route = await provider.GetRouteAsync();

        Assert.Equal([7L], route.SensorIds);
        Assert.Single(route.Coordinates);
    }

    [Fact]
    public async Task RouteProviderFactory_UsesApplicationRoute_WhenFixedRouteIsAbsent()
    {
        var definition = new SimulationScenarioDefinition(
            new SimulationGraphDefinition("scenario.osm"),
            TimeSpan.FromMinutes(1),
            TickCount: 1,
            Sensors: [new SimulationSensorDefinition(7L, 0.4f, 0.1f)],
            AverageFillThreshold: 0.75f,
            CriticalFillThreshold: 0.9f);
        var applicationRoute = new CollectionRouteResponseDto
        {
            DepotCoordinates = new Position(-23.55, -46.63),
            OrderedNodeCoordinates = [new Position(-23.55, -46.63), new Position(-23.56, -46.64)],
            Stops = [],
            SelectedSensors =
            [
                new CollectionRouteSelectedSensorDto
                {
                    SensorId = 7L,
                    NodeId = 10,
                    Position = new Position(-23.56, -46.64),
                    FillLevel = 0.8f,
                    FillTimestamp = DateTime.UtcNow
                }
            ],
            TotalDistance = 1.25,
            RouteGenerationMs = 0.1
        };
        var routingService = new StubCollectionRoutingService(applicationRoute);

        ISimulationRouteProvider provider = SimulationRouteProviderFactory.Create(
            definition,
            routingService);

        SimulationRoute route = await provider.GetRouteAsync();

        Assert.Equal([7L], route.SensorIds);
        Assert.Equal(1.25, route.Distance);
        Assert.Equal(2, route.Coordinates.Count);
        Assert.True(routingService.WasCalled);
    }

    [Fact]
    public async Task Orchestrator_ResolvesDynamicRoute_OnTrigger_AndCollectsSelectedSensor()
    {
        var definition = new SimulationScenarioDefinition(
            new SimulationGraphDefinition("scenario.osm"),
            TimeSpan.FromMinutes(1),
            TickCount: 2,
            Sensors: [new SimulationSensorDefinition(7L, 0.8f, 0.1f, CapacityLiters: 10f)],
            AverageFillThreshold: 0.75f,
            CriticalFillThreshold: 0.9f);
        var provider = new CountingRouteProvider(new SimulationRoute(
            [new Position(-23.55, -46.63), new Position(-23.56, -46.64)],
            [7L],
            1.25));

        SimulationRunResult result = await new SimulationOrchestrator().RunAsync(
            definition,
            provider,
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        CollectionExecutionResult collection = result.Ticks[0].Collection
            ?? throw new InvalidOperationException("Expected a collection on the first tick.");

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(CollectionRouteExecutionMode.ApplicationStrategy, result.Ticks[0].RouteExecution!.Mode);
        Assert.Equal([7L], collection.ServedSensorIds);
        Assert.Equal(8f, collection.CollectedVolumeLiters, precision: 3);
        Assert.Equal(0.1f, result.Ticks[1].Readings[0].FillLevel, precision: 3);
    }

    [Fact]
    public async Task Harness_DisposesEnvironment_AfterSuccessfulRun()
    {
        var definition = CreateHarnessDefinition();
        var factory = new TrackingEnvironmentFactory(new CountingRouteProvider(
            new SimulationRoute([new Position(-23.55, -46.63)], [7L], 0)));
        var harness = new SimulationApplicationHarness(factory, new SimulationOrchestrator());

        SimulationRunResult result = await harness.RunAsync(definition, DateTime.UtcNow);

        Assert.Single(result.Ticks);
        Assert.True(factory.EnvironmentDisposed);
    }

    [Fact]
    public async Task Orchestrator_PublishesReadingsBeforeRouteResolution()
    {
        var definition = CreateHarnessDefinition() with { TickCount = 2 };
        var sink = new RecordingReadingSink();
        var provider = new CountingRouteProvider(new SimulationRoute(
            [new Position(-23.55, -46.63)], [7L], 0));

        SimulationRunResult result = await new SimulationOrchestrator().RunAsync(
            definition,
            provider,
            DateTime.UtcNow,
            readingSink: sink);

        Assert.Equal(2, sink.PublishedReadings.Count);
        Assert.Equal(result.Ticks[0].Readings[0].FillLevel, sink.PublishedReadings[0][0].FillLevel);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Harness_DisposesEnvironment_WhenSimulationFails()
    {
        var definition = CreateHarnessDefinition();
        var factory = new TrackingEnvironmentFactory(new ThrowingRouteProvider());
        var harness = new SimulationApplicationHarness(factory, new SimulationOrchestrator());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.RunAsync(definition, DateTime.UtcNow));

        Assert.True(factory.EnvironmentDisposed);
    }

    [Fact]
    public async Task SqliteEnvironment_ImportsOsmAndRegistersMockSensor()
    {
        string osmPath = Path.Combine(Path.GetTempPath(), $"simulation-{Guid.NewGuid():N}.osm");
        await File.WriteAllTextAsync(osmPath, """
        <osm version="0.6">
          <node id="1" lat="-23.5505" lon="-46.6333" />
          <node id="2" lat="-23.5515" lon="-46.6340" />
          <way id="10">
            <nd ref="1" />
            <nd ref="2" />
            <tag k="highway" v="residential" />
          </way>
        </osm>
        """);

        try
        {
            var definition = new SimulationScenarioDefinition(
                new SimulationGraphDefinition(osmPath),
                TimeSpan.FromMinutes(1),
                TickCount: 1,
                Sensors:
                [
                    new SimulationSensorDefinition(
                        7L,
                        0.8f,
                        0.1f,
                        Position: new Position(-23.5505, -46.6333))
                ],
                AverageFillThreshold: 0.75f,
                CriticalFillThreshold: 0.9f,
                Route: new SimulationRouteDefinition(
                    [new Position(-23.5505, -46.6333)],
                    [7L]));

            await using ISimulationApplicationEnvironment environment =
                await new SqliteSimulationEnvironmentFactory().CreateAsync(definition);

            SimulationRoute route = await environment.RouteProvider.GetRouteAsync();

            Assert.Equal([7L], route.SensorIds);
            Assert.Single(route.Coordinates);
        }
        finally
        {
            File.Delete(osmPath);
        }
    }

    private static SimulationScenarioDefinition CreateHarnessDefinition()
    {
        return new SimulationScenarioDefinition(
            new SimulationGraphDefinition("scenario.osm"),
            TimeSpan.FromMinutes(1),
            TickCount: 1,
            Sensors: [new SimulationSensorDefinition(7L, 0.8f, 0.1f)],
            AverageFillThreshold: 0.75f,
            CriticalFillThreshold: 0.9f);
    }

    private sealed class ThrowingCollectionRoutingService : ICollectionRoutingService
    {
        public Task<CollectionRouteResponseDto> GenerateRouteAsync(
            CollectionRouteRequestOptionsDto? options = null,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Application route generation should not be called.");
    }

    private sealed class StubCollectionRoutingService(CollectionRouteResponseDto route)
        : ICollectionRoutingService
    {
        public bool WasCalled { get; private set; }

        public Task<CollectionRouteResponseDto> GenerateRouteAsync(
            CollectionRouteRequestOptionsDto? options = null,
            CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(route);
        }
    }

    private sealed class CountingRouteProvider(SimulationRoute route) : ISimulationRouteProvider
    {
        public int CallCount { get; private set; }

        public Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(route);
        }
    }

    private sealed class ThrowingRouteProvider : ISimulationRouteProvider
    {
        public Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Route generation failed.");
    }

    private sealed class TrackingEnvironmentFactory(ISimulationRouteProvider routeProvider)
        : ISimulationApplicationEnvironmentFactory
    {
        public bool EnvironmentDisposed { get; private set; }

        public Task<ISimulationApplicationEnvironment> CreateAsync(
            SimulationScenarioDefinition definition,
            CancellationToken ct = default)
        {
            return Task.FromResult<ISimulationApplicationEnvironment>(
                new TrackingEnvironment(routeProvider, () => EnvironmentDisposed = true));
        }
    }

    private sealed class TrackingEnvironment(
        ISimulationRouteProvider routeProvider,
        Action onDispose) : ISimulationApplicationEnvironment
    {
        public ISimulationRouteProvider RouteProvider => routeProvider;
        public ISimulationReadingSink? ReadingSink => null;

        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReadingSink : ISimulationReadingSink
    {
        public List<IReadOnlyList<SimulationSensorReading>> PublishedReadings { get; } = [];

        public Task PublishAsync(
            IReadOnlyList<SimulationSensorReading> readings,
            CancellationToken ct = default)
        {
            PublishedReadings.Add(readings);
            return Task.CompletedTask;
        }
    }
}
