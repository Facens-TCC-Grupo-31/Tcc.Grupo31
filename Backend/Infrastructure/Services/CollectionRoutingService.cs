using Application.Common.Dtos;
using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Infrastructure.Services;

internal sealed class CollectionRoutingService(
    IDepotNodeService depotNodeService,
    IRoutePlanningStrategy routePlanningStrategy,
    IShortestPathStrategy shortestPathStrategy,
    IGraphService graphService,
    AppDbContext db) : ICollectionRoutingService
{
    public async Task<CollectionRouteResponseDto> GenerateRouteAsync(
        CollectionRouteRequestOptionsDto? options = null,
        CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();

        int configuredDepotNodeId = await depotNodeService.GetOrCreateDepotNodeIdAsync(ct);
        GraphSnapshot baseSnapshot = await graphService.GetGraphSnapshotAsync(ct);

        var overlayBuilder = new EphemeralGraphOverlayBuilder(baseSnapshot);

        GraphNode? configuredDepotNode = await db.GraphNodes.FindAsync([configuredDepotNodeId], cancellationToken: ct)
            ?? throw new InvalidOperationException($"Configured depot node {configuredDepotNodeId} not found");

        var configuredDepotPosition = configuredDepotNode.Position;

        (int depotNodeId, int startNodeId, int endNodeId) = await ResolveRouteEndpointNodeIds(options, configuredDepotNodeId, overlayBuilder, configuredDepotPosition, ct);

        var routingGraphSnapshot = overlayBuilder.BuildSnapshot();
        var planningResult = await routePlanningStrategy.PlanAsync(
            startNodeId,
            endNodeId,
            routingGraphSnapshot,
            ct
        );

        Position depotPosition;
        if (overlayBuilder.TryGetVirtualPosition(depotNodeId, out var virtualDepot))
        {
            depotPosition = virtualDepot!;
        }
        else if (configuredDepotNodeId == depotNodeId)
        {
            depotPosition = configuredDepotPosition;
        }
        else
        {
            throw new InvalidOperationException($"Depot node {depotNodeId} not found");
        }

        var orderedNodeIds = planningResult.NodeVisitOrder;

        var expandedNodeIds = await ExpandRouteNodeIdsAsync(orderedNodeIds, routingGraphSnapshot, ct);

        var selectedSensorNodeIds = planningResult.SelectedSensors
            .Select(sensor => sensor.NodeId)
            .Distinct()
            .ToList();

        var nodeIdsToLoad = expandedNodeIds
            .Concat(orderedNodeIds)
            .Concat(selectedSensorNodeIds)
            .Where(nodeId => nodeId > 0)
            .Distinct()
            .ToList();

        var orderedNodes = await db.GraphNodes
            .Where(n => nodeIdsToLoad.Contains(n.Id))
            .ToListAsync(ct);

        var nodePositionMap = orderedNodes.ToDictionary(n => n.Id, n => n.Position);
        foreach ((int nodeId, Position position) in overlayBuilder.GetVirtualPositions())
        {
            nodePositionMap[nodeId] = position;
        }

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

        var stops = orderedNodeIds
            .Select((nodeId, index) => new CollectionRouteStopDto
            {
                StopIndex = index,
                NodeId = nodeId,
                Position = nodePositionMap[nodeId]
            })
            .ToList();

        totalSw.Stop();

        return new CollectionRouteResponseDto
        {
            DepotCoordinates = depotPosition,
            OrderedNodeCoordinates = orderedNodeCoordinates,
            Stops = stops,
            SelectedSensors = selectedSensors,
            TotalDistance = planningResult.TotalDistance,
            RouteGenerationMs = totalSw.Elapsed.TotalMilliseconds
        };
    }

    private async Task<(int depotNodeId, int startNodeId, int endNodeId)> ResolveRouteEndpointNodeIds(CollectionRouteRequestOptionsDto? options, int configuredDepotNodeId, EphemeralGraphOverlayBuilder overlayBuilder, Position configuredDepotPosition, CancellationToken ct)
    {
        var effectiveDepotPosition = options?.DepotPosition ?? configuredDepotPosition;
        var effectiveStartPosition = options?.StartPosition ?? effectiveDepotPosition;
        var effectiveEndPosition = options?.EndPosition ?? effectiveDepotPosition;

        int depotNodeId = await ResolveEndpointNodeIdAsync(
            effectiveDepotPosition,
            configuredDepotPosition,
            configuredDepotNodeId,
            overlayBuilder,
            ct
        );

        int startNodeId = await ResolveEndpointNodeIdAsync(
            effectiveStartPosition,
            configuredDepotPosition,
            configuredDepotNodeId,
            overlayBuilder,
            ct
        );

        int endNodeId = await ResolveEndpointNodeIdAsync(
            effectiveEndPosition,
            configuredDepotPosition,
            configuredDepotNodeId,
            overlayBuilder,
            ct
        );
        return (depotNodeId, startNodeId, endNodeId);
    }

    private async Task<int> ResolveEndpointNodeIdAsync(
        Position endpointPosition,
        Position configuredDepotPosition,
        int configuredDepotNodeId,
        EphemeralGraphOverlayBuilder overlayBuilder,
        CancellationToken ct)
    {
        if (PositionsEqual(endpointPosition, configuredDepotPosition))
        {
            return configuredDepotNodeId;
        }

        if (overlayBuilder.TryGetNodeIdByPosition(endpointPosition, out int existingNodeId))
        {
            return existingNodeId;
        }

        GraphEdgeProjection projection = await graphService.ProjectOntoNearestEdgeAsync(endpointPosition, ct);
        return overlayBuilder.AddProjectedNode(projection);
    }

    private async Task<List<int>> ExpandRouteNodeIdsAsync(
        List<int> nodeVisitOrder,
        GraphSnapshot snapshot,
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

            var legPath = await shortestPathStrategy.GetShortestPathAsync(from, to, snapshot, ct);
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

    private static List<Position> CollapseConsecutiveDuplicateCoordinates(List<Position> coordinates)
    {
        if (coordinates.Count <= 1)
        {
            return coordinates;
        }

        var deduplicated = new List<Position>(coordinates.Count) { coordinates[0] };
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

    private static bool PositionsEqual(Position a, Position b)
    {
        return a.Latitude == b.Latitude && a.Longitude == b.Longitude;
    }

    private sealed class EphemeralGraphOverlayBuilder
    {
        private readonly HashSet<int> _nodeIds;
        private readonly Dictionary<int, List<GraphNeighbor>> _adjacency;
        private readonly Dictionary<int, Position> _virtualNodePositions = [];
        private readonly Dictionary<string, int> _nodeByPositionKey = [];
        private int _nextVirtualNodeId = -1;
        private int _nextVirtualEdgeId = -1;

        public EphemeralGraphOverlayBuilder(GraphSnapshot baseSnapshot)
        {
            _nodeIds = [.. baseSnapshot.NodeIds];
            _adjacency = baseSnapshot.AdjacencyByNode.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList());
        }

        public bool TryGetNodeIdByPosition(Position position, out int nodeId)
            => _nodeByPositionKey.TryGetValue(BuildPositionKey(position), out nodeId);

        public bool TryGetVirtualPosition(int nodeId, out Position? position)
        {
            if (_virtualNodePositions.TryGetValue(nodeId, out Position? found))
            {
                position = found;
                return true;
            }

            position = null;
            return false;
        }

        public Dictionary<int, Position> GetVirtualPositions() => _virtualNodePositions;

        public int AddProjectedNode(GraphEdgeProjection projection)
        {
            int nodeId = _nextVirtualNodeId--;
            _nodeIds.Add(nodeId);
            _virtualNodePositions[nodeId] = projection.ProjectedPosition;
            _nodeByPositionKey[BuildPositionKey(projection.ProjectedPosition)] = nodeId;

            EnsureNode(projection.FromNodeId);
            EnsureNode(projection.ToNodeId);
            EnsureNode(nodeId);

            AddDirectedEdge(projection.FromNodeId, nodeId, projection.DistanceToFromNode);
            AddDirectedEdge(nodeId, projection.ToNodeId, projection.DistanceToToNode);

            if (projection.HasReverseEdge)
            {
                AddDirectedEdge(projection.ToNodeId, nodeId, projection.DistanceToToNode);
                AddDirectedEdge(nodeId, projection.FromNodeId, projection.DistanceToFromNode);
            }

            return nodeId;
        }

        public GraphSnapshot BuildSnapshot()
        {
            var finalizedAdjacency = _adjacency.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<GraphNeighbor>)kvp.Value
                    .OrderBy(neighbor => neighbor.ToNodeId)
                    .ThenBy(neighbor => neighbor.EdgeId)
                    .ToList());

            return new GraphSnapshot(_nodeIds.OrderBy(id => id).ToList(), finalizedAdjacency);
        }

        private void EnsureNode(int nodeId)
        {
            if (!_adjacency.ContainsKey(nodeId))
            {
                _adjacency[nodeId] = [];
            }
        }

        private void AddDirectedEdge(int fromNodeId, int toNodeId, double distance)
        {
            _adjacency[fromNodeId].Add(new GraphNeighbor(toNodeId, _nextVirtualEdgeId--, distance));
        }

        private static string BuildPositionKey(Position position)
        {
            return $"{position.Latitude:R}|{position.Longitude:R}";
        }
    }
}
