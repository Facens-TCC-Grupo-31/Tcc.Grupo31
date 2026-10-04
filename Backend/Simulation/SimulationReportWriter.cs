using System.Text.Json;
using Domain.ValueObjects;

namespace Simulation;

public static class SimulationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteAsync(
        string filePath,
        SimulationRunResult result,
        string scenarioName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new SimulationReportDto(
            scenarioName,
            DateTime.UtcNow,
            new SimulationSummaryDto(
                result.SimulationDuration,
                result.CollectionCount,
                result.OverflowEventCount,
                result.TotalOverflowDuration,
                result.MaximumSingleSensorOverflowDuration,
                result.TotalCollectedVolumeLiters,
                result.TotalRouteDistance),
            result.OverflowEvents.Select(@event => new SimulationOverflowEventDto(
                @event.SensorId,
                @event.StartedAt,
                @event.EndedAt,
                @event.Duration)).ToArray(),
            result.Ticks.Select(tick => new SimulationTickReportDto(
                tick.Timestamp,
                new SimulationTickStateDto(
                    tick.TriggerContext.ActiveSensorCount,
                    tick.TriggerContext.AverageFillLevel,
                    tick.TriggerContext.CriticalSensorIds,
                    tick.Readings.Select(reading => new SimulationReadingReportDto(
                        reading.SensorId,
                        reading.FillLevel,
                        reading.Timestamp)).ToArray()),
                tick.CollectionTriggered,
                tick.RouteExecution is null
                    ? null
                    : new SimulationRouteReportDto(
                        tick.RouteExecution.Mode.ToString(),
                        tick.RouteExecution.FixedRouteCoordinates.Select(ToCoordinate).ToArray()),
                tick.Collection is null
                    ? null
                    : new SimulationCollectionReportDto(
                        tick.Collection.ServedSensorIds,
                        tick.Collection.CollectedVolumeLiters,
                        tick.Collection.RouteDistance))).ToArray());

        await using FileStream stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, ct);
    }

    private static SimulationCoordinateDto ToCoordinate(Position position) =>
        new(position.Latitude, position.Longitude);

    private sealed record SimulationReportDto(
        string ScenarioName,
        DateTime GeneratedAtUtc,
        SimulationSummaryDto Summary,
        IReadOnlyList<SimulationOverflowEventDto> OverflowEvents,
        IReadOnlyList<SimulationTickReportDto> Ticks);

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
        SimulationRouteReportDto? Route,
        SimulationCollectionReportDto? Collection);

    private sealed record SimulationTickStateDto(
        int ActiveSensorCount,
        float AverageFillLevel,
        IReadOnlyList<long> ActiveCriticalSensorIds,
        IReadOnlyList<SimulationReadingReportDto> Readings);

    private sealed record SimulationReadingReportDto(
        long SensorId,
        float FillLevel,
        DateTime Timestamp);

    private sealed record SimulationRouteReportDto(
        string Mode,
        IReadOnlyList<SimulationCoordinateDto> Coordinates);

    private sealed record SimulationCollectionReportDto(
        IReadOnlyList<long> ServedSensorIds,
        float CollectedVolumeLiters,
        double RouteDistance);

    private sealed record SimulationCoordinateDto(double Latitude, double Longitude);
}