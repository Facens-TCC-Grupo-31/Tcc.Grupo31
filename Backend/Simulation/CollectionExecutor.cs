using Application.Common.Utils;
using Domain.ValueObjects;

namespace Simulation;

public sealed record CollectionExecutionResult(
    IReadOnlyList<long> ServedSensorIds,
    float CollectedVolumeLiters,
    double RouteDistanceKilometers
);

public sealed class CollectionExecutor
{
    private readonly LocalApproximateDistanceCalculator _distanceCalculator = new();

    public CollectionExecutionResult Execute(
        IReadOnlyList<SimulationSensorState> sensors,
        IReadOnlyList<long> servedSensorIds,
        IReadOnlyList<Position> routeCoordinates)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(servedSensorIds);
        ArgumentNullException.ThrowIfNull(routeCoordinates);

        var servedIds = servedSensorIds.Distinct().ToArray();
        var servedSensors = sensors
            .Where(sensor => servedIds.Contains(sensor.SensorId))
            .ToList();

        float collectedVolumeLiters = servedSensors.Sum(sensor => sensor.CurrentVolumeLiters);

        foreach (SimulationSensorState sensor in servedSensors)
        {
            sensor.CurrentFillLevel = 0;
            sensor.CurrentVolumeLiters = 0;
        }

        return new CollectionExecutionResult(
            servedSensors.Select(sensor => sensor.SensorId).ToList(),
            collectedVolumeLiters,
            ToKilometers(CalculateRouteDistanceMeters(routeCoordinates))
        );
    }

    private double CalculateRouteDistanceMeters(IReadOnlyList<Position> coordinates)
    {
        double totalDistanceMeters = 0;

        for (var index = 1; index < coordinates.Count; index++)
        {
            totalDistanceMeters += _distanceCalculator.CalculateDistanceMeters(coordinates[index - 1], coordinates[index]);
        }

        return totalDistanceMeters;
    }

    private static double ToKilometers(double meters) => meters / 1000.0;
}