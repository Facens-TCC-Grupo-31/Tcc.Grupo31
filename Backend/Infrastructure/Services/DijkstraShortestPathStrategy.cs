using Application.Services;

namespace Infrastructure.Services;

internal sealed class DijkstraShortestPathStrategy(
    IGraphService graphService) : IShortestPathStrategy
{
    public async Task<IReadOnlyDictionary<int, double>> GetShortestDistancesAsync(
        int sourceNodeId,
        IReadOnlyCollection<int> targetNodeIds,
        CancellationToken ct = default)
    {
        GraphSnapshot snapshot = await graphService.GetGraphSnapshotAsync(ct);
        return ComputeShortestDistances(sourceNodeId, targetNodeIds, snapshot, ct);
    }

    public async Task<IReadOnlyDictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        IReadOnlyCollection<int> waypointNodeIds,
        CancellationToken ct = default)
    {
        GraphSnapshot snapshot = await graphService.GetGraphSnapshotAsync(ct);
        var waypoints = waypointNodeIds
            .Distinct()
            .OrderBy(nodeId => nodeId)
            .ToList();

        var matrix = new Dictionary<(int From, int To), double>(waypoints.Count * waypoints.Count);

        foreach (int sourceNodeId in waypoints)
        {
            IReadOnlyDictionary<int, double> distances = ComputeShortestDistances(sourceNodeId, waypoints, snapshot, ct);
            foreach ((int targetNodeId, double distance) in distances)
            {
                matrix[(sourceNodeId, targetNodeId)] = distance;
            }
        }

        return matrix;
    }

    private static IReadOnlyDictionary<int, double> ComputeShortestDistances(
        int sourceNodeId,
        IReadOnlyCollection<int> targetNodeIds,
        GraphSnapshot snapshot,
        CancellationToken ct)
    {
        if (!snapshot.NodeIds.Contains(sourceNodeId))
        {
            throw new InvalidOperationException($"Source node {sourceNodeId} does not exist in graph.");
        }

        var distances = snapshot.NodeIds.ToDictionary(nodeId => nodeId, _ => double.PositiveInfinity);
        distances[sourceNodeId] = 0;

        var targets = targetNodeIds.ToHashSet();
        int remainingTargets = targets.Count;

        var queue = new PriorityQueue<int, (double Distance, int NodeId)>();
        queue.Enqueue(sourceNodeId, (0, sourceNodeId));

        while (queue.TryDequeue(out int currentNodeId, out (double Distance, int NodeId) priority))
        {
            ct.ThrowIfCancellationRequested();

            double knownDistance = distances[currentNodeId];
            if (priority.Distance > knownDistance)
            {
                continue;
            }

            if (targets.Contains(currentNodeId))
            {
                remainingTargets--;
                targets.Remove(currentNodeId);
                if (remainingTargets == 0)
                {
                    break;
                }
            }

            if (!snapshot.AdjacencyByNode.TryGetValue(currentNodeId, out IReadOnlyList<GraphNeighbor>? outgoingEdges))
            {
                continue;
            }

            foreach (GraphNeighbor edge in outgoingEdges)
            {
                double newDistance = knownDistance + edge.Distance;
                if (newDistance >= distances[edge.ToNodeId])
                {
                    continue;
                }

                distances[edge.ToNodeId] = newDistance;
                queue.Enqueue(edge.ToNodeId, (newDistance, edge.ToNodeId));
            }
        }

        var result = new Dictionary<int, double>(targetNodeIds.Count);
        foreach (int targetNodeId in targetNodeIds)
        {
            if (distances.TryGetValue(targetNodeId, out double distance))
            {
                result[targetNodeId] = distance;
            }
        }

        return result;
    }
}
