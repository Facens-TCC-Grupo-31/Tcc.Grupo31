using Application.Common.Utils;
using Application.Services;
using Domain.ValueObjects;

namespace Simulation;

public sealed record SimulationRoute(
    IReadOnlyList<Position> Coordinates,
    IReadOnlyList<long> SensorIds,
    double DistanceKilometers);

public interface ISimulationRouteProvider
{
    Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default);
}

public sealed class FixedSimulationRouteProvider(SimulationRouteDefinition definition) : ISimulationRouteProvider
{
    public Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var coordinates = definition.Coordinates;

        return Task.FromResult(new SimulationRoute(
            coordinates,
            definition.SensorIds,
            CalculateTotalDistanceKilometers(coordinates)));
    }

    private static double CalculateTotalDistanceKilometers(
        IReadOnlyList<Position> coordinates)
    {
        double totalDistanceMeters = 0;

        for (var index = 1; index < coordinates.Count; index++)
        {
            totalDistanceMeters += new LocalApproximateDistanceCalculator().CalculateDistanceMeters(coordinates[index - 1], coordinates[index]);
        }

        return totalDistanceMeters / 1000.0;
    }
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