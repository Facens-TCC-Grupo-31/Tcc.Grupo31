using Domain.ValueObjects;

namespace Simulation;

public sealed record CollectionExecutionResult(
    IReadOnlyList<long> ServedSensorIds,
    float CollectedVolumeLiters,
    double RouteDistance);

public sealed class CollectionExecutor
{
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
            servedSensors.Select(sensor => sensor.SensorId).ToArray(),
            collectedVolumeLiters,
            CalculateRouteDistance(routeCoordinates));
    }

    private static double CalculateRouteDistance(IReadOnlyList<Position> coordinates)
    {
        double totalDistanceKm = 0;

        for (var index = 1; index < coordinates.Count; index++)
        {
            totalDistanceKm += HaversineDistanceKm(coordinates[index - 1], coordinates[index]);
        }

        return totalDistanceKm;
    }

    private static double HaversineDistanceKm(Position first, Position second)
    {
        const double earthRadiusKm = 6371.0088;
        double latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        double longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        double firstLatitude = DegreesToRadians(first.Latitude);
        double secondLatitude = DegreesToRadians(second.Latitude);

        double haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(firstLatitude) * Math.Cos(secondLatitude)
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);

        return earthRadiusKm * 2 * Math.Asin(Math.Sqrt(haversine));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}