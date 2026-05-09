using Application.Services;
using System.Diagnostics;

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

        var matrixSw = Stopwatch.StartNew();
        IReadOnlyDictionary<(int From, int To), double> matrix =
            await shortestPathStrategy.BuildDistanceMatrixAsync(matrixNodes, ct);
        matrixSw.Stop();

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

        var finiteDistances = matrix
            .Where(x => x.Key.From != x.Key.To && !double.IsInfinity(x.Value))
            .Select(x => x.Value)
            .ToList();

        return new RoutePlanningResult(
            nodeVisitOrder,
            sensorVisitOrder,
            selectedPoints.Count,
            sensorVisitOrder.Count,
            totalDistance,
            finiteDistances.Count == 0 ? 0 : finiteDistances.Average(),
            matrixSw.Elapsed.TotalMilliseconds);
    }
}
