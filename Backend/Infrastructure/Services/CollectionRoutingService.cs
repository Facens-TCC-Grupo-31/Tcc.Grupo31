using Application.Common.Dtos;
using Application.Services;
using Infrastructure.Database;
using Infrastructure.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Infrastructure.Services;

internal sealed class CollectionRoutingService(
    IDepotNodeService depotNodeService,
    IRoutePlanningStrategy routePlanningStrategy,
    IShortestPathStrategy shortestPathStrategy,
    AppDbContext db,
    IOptions<RoutingOptions> options) : ICollectionRoutingService
{
    private readonly RoutingOptions _options = options.Value;

    public async Task<CollectionRouteResponseDto> GenerateRouteAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();

        int depotNodeId = await depotNodeService.GetOrCreateDepotNodeIdAsync(ct);
        RoutePlanningResult planningResult = await routePlanningStrategy.PlanAsync(
            new RoutePlanningRequest(depotNodeId, _options.FillThreshold),
            ct);

        // Fetch depot node position
        var depotNode = await db.GraphNodes.FindAsync(new object[] { depotNodeId }, cancellationToken: ct);
        if (depotNode == null)
            throw new InvalidOperationException($"Depot node {depotNodeId} not found");

        var orderedNodeIds = planningResult.NodeVisitOrder;
        var expandedNodeIds = await ExpandRouteNodeIdsAsync(orderedNodeIds, ct);

        var selectedSensorNodeIds = planningResult.SelectedSensors
            .Select(sensor => sensor.NodeId)
            .Distinct()
            .ToList();

        var nodeIdsToLoad = expandedNodeIds
            .Concat(selectedSensorNodeIds)
            .Distinct()
            .ToList();

        var orderedNodes = await db.GraphNodes
            .Where(n => nodeIdsToLoad.Contains(n.Id))
            .ToListAsync(ct);

        var nodePositionMap = orderedNodes.ToDictionary(n => n.Id, n => n.Position);
        var orderedNodeCoordinates = expandedNodeIds
            .Select(nodeId => nodePositionMap[nodeId])
            .ToList();

        orderedNodeCoordinates = CollapseConsecutiveDuplicateCoordinates(orderedNodeCoordinates);

        var selectedSensors = planningResult.SelectedSensors
            .Select(sensor => new CollectionRouteSelectedSensorDto
            {
                SensorId = sensor.SensorId,
                NodeId = sensor.NodeId,
                Position = nodePositionMap[sensor.NodeId],
                FillLevel = sensor.FillLevel,
                FillTimestamp = sensor.FillTimestamp
            })
            .ToList();

        totalSw.Stop();

        return new CollectionRouteResponseDto
        {
            DepotCoordinates = depotNode.Position,
            OrderedNodeCoordinates = orderedNodeCoordinates,
            SelectedSensors = selectedSensors,
            TotalDistance = planningResult.TotalDistance,
            RouteGenerationMs = totalSw.Elapsed.TotalMilliseconds
        };
    }

    private async Task<IReadOnlyList<int>> ExpandRouteNodeIdsAsync(
        IReadOnlyList<int> nodeVisitOrder,
        CancellationToken ct)
    {
        if (nodeVisitOrder.Count == 0)
        {
            return [];
        }

        var expandedNodeIds = new List<int> { nodeVisitOrder[0] };

        for (int i = 0; i < nodeVisitOrder.Count - 1; i++)
        {
            int from = nodeVisitOrder[i];
            int to = nodeVisitOrder[i + 1];

            IReadOnlyList<int> legPath = await shortestPathStrategy.GetShortestPathAsync(from, to, ct);
            if (legPath.Count == 0)
            {
                throw new InvalidOperationException($"Missing shortest path from node {from} to node {to}.");
            }

            if (from == to)
            {
                expandedNodeIds.Add(to);
                continue;
            }

            expandedNodeIds.AddRange(legPath.Skip(1));
        }

        return expandedNodeIds;
    }

    private static List<Domain.ValueObjects.Position> CollapseConsecutiveDuplicateCoordinates(
        IReadOnlyList<Domain.ValueObjects.Position> coordinates)
    {
        if (coordinates.Count <= 1)
        {
            return coordinates.ToList();
        }

        var deduplicated = new List<Domain.ValueObjects.Position>(coordinates.Count) { coordinates[0] };
        for (int i = 1; i < coordinates.Count; i++)
        {
            if (coordinates[i] == deduplicated[^1])
            {
                continue;
            }

            deduplicated.Add(coordinates[i]);
        }

        return deduplicated;
    }
}
