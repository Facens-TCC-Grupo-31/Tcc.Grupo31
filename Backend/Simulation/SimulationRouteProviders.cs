using Application.Services;

namespace Simulation;

public sealed record SimulationRoute(
    IReadOnlyList<Domain.ValueObjects.Position> Coordinates,
    IReadOnlyList<long> SensorIds,
    double Distance);

public interface ISimulationRouteProvider
{
    Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default);
}

public sealed class FixedSimulationRouteProvider(SimulationRouteDefinition definition)
    : ISimulationRouteProvider
{
    public Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var coordinates = definition.Coordinates;
        return Task.FromResult(new SimulationRoute(
            coordinates,
            definition.SensorIds,
            CalculateDistance(coordinates)));
    }

    private static double CalculateDistance(
        IReadOnlyList<Domain.ValueObjects.Position> coordinates)
    {
        double totalDistance = 0;

        for (var index = 1; index < coordinates.Count; index++)
        {
            totalDistance += HaversineDistanceKm(coordinates[index - 1], coordinates[index]);
        }

        return totalDistance;
    }

    private static double HaversineDistanceKm(
        Domain.ValueObjects.Position first,
        Domain.ValueObjects.Position second)
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

public static class SimulationRouteProviderFactory
{
    public static ISimulationRouteProvider Create(
        SimulationScenarioDefinition definition,
        ICollectionRoutingService? routingService = null,
        IReadOnlyDictionary<long, long>? logicalToApplicationSensorIds = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Route is not null)
        {
            return new FixedSimulationRouteProvider(definition.Route);
        }

        return routingService is null
            ? throw new ArgumentNullException(nameof(routingService),
                "An application routing service is required when the scenario has no fixed route.")
            : new InMemoryApplicationRouteProvider(routingService, logicalToApplicationSensorIds);
    }
}