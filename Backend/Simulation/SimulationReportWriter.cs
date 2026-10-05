using Domain.ValueObjects;
using System.Collections;
using System.Text.Json;

namespace Simulation;

public interface ISimulationReportWriter
{
    Task<string> WriteAsync(
        SimulationRunResult result,
        string scenarioName,
        CancellationToken ct = default);
}

public class SimulationReportWriter : ISimulationReportWriter
{
    private const string ReportFileNameFormat = "simulation-{0}-{1:yyyyMMddHHmmss}.json";

    private static readonly string _logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Simulation", "logs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> WriteAsync(
        SimulationRunResult result,
        string scenarioName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);

        string filePath = Path.Combine(_logsDirectory, GetReportFileName(scenarioName));

        Directory.CreateDirectory(_logsDirectory);

        var report = new SimulationReportDto(
            scenarioName,
            DateTime.UtcNow,
            MapSummary(result),
            result.OverflowEvents.Select(MapOverflowEvent),
            result.Ticks.Select(MapTick)
        );

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, ct);

        return filePath;
    }

    private static SimulationSummaryDto MapSummary(SimulationRunResult result) =>
        new(result.SimulationDuration,
            result.CollectionCount,
            result.OverflowEventCount,
            result.TotalOverflowDuration,
            result.MaximumSingleSensorOverflowDuration,
            result.TotalCollectedVolumeLiters,
            result.TotalRouteDistance);

    private static SimulationOverflowEventDto MapOverflowEvent(SimulationOverflowEvent @event) =>
        new(@event.SensorId, @event.StartedAt, @event.EndedAt, @event.Duration);

    private static SimulationTickReportDto MapTick(SimulationTickResult tick) =>
        new(tick.Timestamp,
            MapTickState(tick),
            tick.CollectionTriggered,
            tick.RouteExecution is null ? null : MapRoute(tick.RouteExecution, tick.Collection),
            tick.Collection is null ? null : MapCollection(tick.Collection));

    private static SimulationTickStateDto MapTickState(SimulationTickResult tick) =>
        new(tick.TriggerContext.ActiveSensorCount,
            tick.TriggerContext.AverageFillLevel,
            tick.TriggerContext.CriticalSensorIds,
            tick.Readings.Select(MapReading));

    private static SimulationReadingReportDto MapReading(SimulationSensorReading reading) =>
        new(reading.SensorId, reading.FillLevel, reading.Timestamp);

    private static GeoJsonFeatureCollectionDto MapRoute(
        CollectionRouteExecutionDecision route,
        CollectionExecutionResult? collection)
    {
        var features = new List<GeoJsonFeatureDto>
        {
            new(
                Geometry: new GeoJsonLineStringDto(route.FixedRouteCoordinates.Select(p => new[] { p.Longitude, p.Latitude })),
                Properties: new { Mode = route.Mode.ToString() }
            )
        };

        var coords = route.FixedRouteCoordinates;
        if (coords.Count > 0)
        {
            var start = coords[0];
            var end = coords[^1];

            bool isSameStartEnd = Math.Abs(start.Latitude - end.Latitude) < 0.000001 &&
                                  Math.Abs(start.Longitude - end.Longitude) < 0.000001;

            if (isSameStartEnd)
            {
                features.Add(CreatePointFeature(start, "Start/End"));
            }
            else
            {
                features.Add(CreatePointFeature(start, "Start"));
                features.Add(CreatePointFeature(end, "End"));
            }

            if (collection is not null && collection.ServedSensorIds.Any())
            {
                int stopIndex = 1;
                foreach (var (sensorId, pos) in collection.ServedSensorIds.Zip(coords.Skip(1)))
                {
                    features.Add(CreatePointFeature(pos, $"{stopIndex} (SensorId: {sensorId})"));
                    stopIndex++;
                }
            }
        }

        return new GeoJsonFeatureCollectionDto(features);
    }

    private static GeoJsonFeatureDto CreatePointFeature(Position position, string name) =>
        new(
            Geometry: new GeoJsonPointDto(new[] { position.Longitude, position.Latitude }),
            Properties: new { Name = name }
        );

    private static SimulationCollectionReportDto MapCollection(CollectionExecutionResult collection) =>
        new(collection.ServedSensorIds, collection.CollectedVolumeLiters, collection.RouteDistanceKilometers);

    private static string GetReportFileName(string scenarioName) =>
        string.Format(ReportFileNameFormat, scenarioName.ToLowerInvariant(), DateTime.Now);

    private sealed record SimulationReportDto(
        string ScenarioName,
        DateTime GeneratedAtUtc,
        SimulationSummaryDto Summary,
        IEnumerable OverflowEvents,
        IEnumerable Ticks);

    private sealed record SimulationSummaryDto(
        TimeSpan SimulationDuration,
        int CollectionCount,
        int OverflowEventCount,
        TimeSpan TotalOverflowDuration,
        TimeSpan MaximumSingleSensorOverflowDuration,
        float TotalCollectedVolumeLiters,
        double TotalRouteDistance);

    private sealed record SimulationOverflowEventDto(
        long SensorId,
        DateTime StartedAt,
        DateTime EndedAt,
        TimeSpan Duration);

    private sealed record SimulationTickReportDto(
        DateTime Timestamp,
        SimulationTickStateDto State,
        bool CollectionTriggered,
        GeoJsonFeatureCollectionDto? Route,
        SimulationCollectionReportDto? Collection);

    private sealed record SimulationTickStateDto(
        int ActiveSensorCount,
        float AverageFillLevel,
        IEnumerable ActiveCriticalSensorIds,
        IEnumerable Readings);

    private sealed record SimulationReadingReportDto(
        long SensorId,
        float FillLevel,
        DateTime Timestamp);

    private sealed record GeoJsonFeatureCollectionDto(
        IEnumerable Features,
        string Type = "FeatureCollection");

    private sealed record GeoJsonFeatureDto(
        object Geometry,
        object Properties,
        string Type = "Feature");

    private sealed record GeoJsonLineStringDto(
        IEnumerable Coordinates,
        string Type = "LineString");

    private sealed record GeoJsonPointDto(
        IEnumerable Coordinates,
        string Type = "Point");

    private sealed record SimulationCollectionReportDto(
        IEnumerable ServedSensorIds,
        float CollectedVolumeLiters,
        double RouteDistance);
}