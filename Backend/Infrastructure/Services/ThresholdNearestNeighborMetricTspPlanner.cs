using Application.Services;

namespace Infrastructure.Services;

internal sealed class ThresholdNearestNeighborMetricTspPlanner(
    ThresholdPointSelectionStrategy thresholdPointSelectionStrategy,
    DijkstraShortestPathStrategy dijkstraShortestPathStrategy,
    AsymmetricTwoOptOrderingStrategy nearestNeighborMetricTspOrderingStrategy // Placeholder... two-opt is utilized but should get it's own separate implementation.
    ) : IRoutePlanningStrategy
{
    public async Task<RoutePlanningResult> PlanAsync(
        int startNodeId,
        int endNodeId,
        GraphSnapshot? snapshot,
        CancellationToken ct = default)
    {
        var routePoints = await thresholdPointSelectionStrategy.SelectPointsAsync(ct);

        var targetNodeIds = routePoints
            .Select(point => point.NodeId)
            .Distinct()
            .ToList();

        List<int> matrixNodeIds = [startNodeId, .. targetNodeIds, endNodeId];
        var distanceMatrix = await BuildDistanceMatrixAsync(matrixNodeIds, snapshot, ct);

        var nodeIdsInVisitationOrder = nearestNeighborMetricTspOrderingStrategy.BuildRoute(
            startNodeId,
            endNodeId,
            targetNodeIds,
            (from, to) => distanceMatrix.GetValueOrDefault((from, to))
        );

        var sensorsByNode = routePoints
            .OrderBy(p => p.SensorId)
            .ToLookup(p => p.NodeId);

        var orderedSelectedSensors = nodeIdsInVisitationOrder
            .Where(nodeId => nodeId != startNodeId && nodeId != endNodeId)
            .SelectMany(nodeId => sensorsByNode[nodeId])
            .ToList();

        double totalDistance = CalculateTotalDistance(distanceMatrix, nodeIdsInVisitationOrder);

        return new RoutePlanningResult(
            nodeIdsInVisitationOrder,
            orderedSelectedSensors,
            totalDistance
        );
    }

    private async Task<Dictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        List<int> nodeIds,
        GraphSnapshot? graphSnapshot,
        CancellationToken ct = default)
        => graphSnapshot is null
        ? await dijkstraShortestPathStrategy.BuildDistanceMatrixAsync(nodeIds, ct)
        : await dijkstraShortestPathStrategy.BuildDistanceMatrixAsync(nodeIds, graphSnapshot, ct);

    private static double CalculateTotalDistance(
        Dictionary<(int From, int To), double> matrix,
        List<int> routePointsInVisitationOrder)
    {
        var totalDistance = 0.0;
        for (int i = 0; i < routePointsInVisitationOrder.Count - 1; i++)
        {
            var from = routePointsInVisitationOrder[i];
            var to = routePointsInVisitationOrder[i + 1];

            if (!matrix.TryGetValue((from, to), out var distance))
            {
                throw new InvalidOperationException($"Missing shortest path distance from node {from} to node {to}.");
            }

            totalDistance += distance;
        }

        return totalDistance;
    }
}