using Application.Common.Dtos;
using Application.Services;

namespace Simulation;

public sealed class InMemoryApplicationRouteProvider(
    ICollectionRoutingService routingService,
    IReadOnlyDictionary<long, long>? logicalToApplicationSensorIds = null) : ISimulationRouteProvider
{
    private readonly Dictionary<long, long>? _applicationToLogicalSensorIds = logicalToApplicationSensorIds?.ToDictionary(pair => pair.Value, pair => pair.Key);

    public async Task<SimulationRoute> GetRouteAsync(CancellationToken ct = default)
    {
        CollectionRouteResponseDto route = await routingService.GenerateRouteAsync(ct: ct);

        return new SimulationRoute(
            route.OrderedNodeCoordinates,
            route.OrderedSelectedSensors.Select(sensor => ToLogicalSensorId(sensor.SensorId)).ToList(),
            route.TotalDistanceMeters / 1000
        );
    }

    private long ToLogicalSensorId(long applicationSensorId)
    {
        if (_applicationToLogicalSensorIds is null)
        {
            return applicationSensorId;
        }

        return _applicationToLogicalSensorIds.TryGetValue(applicationSensorId, out long logicalSensorId)
            ? logicalSensorId
            : throw new InvalidOperationException(
                $"Application route selected unknown sensor ID {applicationSensorId}.");
    }
}
