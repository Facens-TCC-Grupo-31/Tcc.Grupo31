using Application.Services;

namespace Infrastructure.Services;

internal sealed class ThresholdNearestNeighborMetricTspPlanner(
    IPointSelectionStrategy pointSelectionStrategy,
    IShortestPathStrategy shortestPathStrategy,
    IRouteOrderingStrategy routeOrderingStrategy) : IRoutePlanningStrategy
{
    public async Task<RoutePlanningResult> PlanAsync(RoutePlanningRequest request, CancellationToken ct = default)
    {
        IReadOnlyList<SelectedCollectionPoint> selectedPoints =
            await pointSelectionStrategy.SelectPointsAsync(request.FillThreshold, ct);

        IReadOnlyList<int> targetNodeIds = selectedPoints
            .Select(point => point.NodeId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var matrixNodes = new List<int>(targetNodeIds.Count + 1) { request.DepotNodeId };
        matrixNodes.AddRange(targetNodeIds);

        IReadOnlyDictionary<(int From, int To), double> matrix =
            await shortestPathStrategy.BuildDistanceMatrixAsync(matrixNodes, ct);

        IReadOnlyList<int> nodeVisitOrder = routeOrderingStrategy.BuildRoute(
            request.DepotNodeId,
            targetNodeIds,
            (from, to) => matrix.TryGetValue((from, to), out double value) ? value : null);

        var nodeToSensors = selectedPoints
            .GroupBy(x => x.NodeId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<long>)g.Select(x => x.SensorId).OrderBy(id => id).ToList());

        var sensorVisitOrder = new List<long>(selectedPoints.Count);
        foreach (int nodeId in nodeVisitOrder)
        {
            if (nodeId == request.DepotNodeId)
            {
                continue;
            }

            if (!nodeToSensors.TryGetValue(nodeId, out IReadOnlyList<long>? sensors) || sensors.Count == 0)
            {
                continue;
            }

            sensorVisitOrder.AddRange(sensors);
        }

        var selectedSensorById = selectedPoints.ToDictionary(x => x.SensorId);
        var orderedSelectedSensors = sensorVisitOrder
            .Where(sensorId => selectedSensorById.ContainsKey(sensorId))
            .Select(sensorId => selectedSensorById[sensorId])
            .ToList();

        double totalDistance = 0;
        for (int i = 0; i < nodeVisitOrder.Count - 1; i++)
        {
            int from = nodeVisitOrder[i];
            int to = nodeVisitOrder[i + 1];
            if (!matrix.TryGetValue((from, to), out double distance))
            {
                throw new InvalidOperationException($"Missing shortest path distance from node {from} to node {to}.");
            }

            totalDistance += distance;
        }

        return new RoutePlanningResult(
            nodeVisitOrder,
            orderedSelectedSensors,
            totalDistance);
    }
}
