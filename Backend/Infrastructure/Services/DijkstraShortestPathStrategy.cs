using Application.Services;

namespace Infrastructure.Services;

internal sealed class DijkstraShortestPathStrategy(IGraphService graphService) : IShortestPathStrategy
{
    public async Task<Dictionary<int, double>> GetShortestDistancesAsync(
        int sourceNodeId,
        List<int> targetNodeIds,
        CancellationToken ct = default)
    {
        GraphSnapshot snapshot = await graphService.GetGraphSnapshotAsync(ct);
        return ComputeShortestDistances(sourceNodeId, targetNodeIds, snapshot, ct);
    }

    public async Task<List<int>> GetShortestPathAsync(
        int sourceNodeId,
        int targetNodeId,
        CancellationToken ct = default)
    {
        GraphSnapshot snapshot = await graphService.GetGraphSnapshotAsync(ct);
        return ComputeShortestPath(sourceNodeId, targetNodeId, snapshot, ct);
    }

    public Task<List<int>> GetShortestPathAsync(
        int sourceNodeId,
        int targetNodeId,
        GraphSnapshot snapshot,
        CancellationToken ct = default)
    {
        return Task.FromResult(ComputeShortestPath(sourceNodeId, targetNodeId, snapshot, ct));
    }

    public async Task<Dictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        List<int> waypointNodeIds,
        CancellationToken ct = default)
    {
        GraphSnapshot snapshot = await graphService.GetGraphSnapshotAsync(ct);
        return await BuildDistanceMatrixAsync(waypointNodeIds, snapshot, ct);
    }

    public Task<Dictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        List<int> waypointNodeIds,
        GraphSnapshot snapshot,
        CancellationToken ct = default)
    {
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

        return Task.FromResult(matrix);
    }

    private static Dictionary<int, double> ComputeShortestDistances(
        int sourceNodeId,
        List<int> targetNodeIds,
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

        var result = new Dictionary<int, double>(targetNodeIds.Count());
        foreach (int targetNodeId in targetNodeIds)
        {
            if (distances.TryGetValue(targetNodeId, out double distance))
            {
                result[targetNodeId] = distance;
            }
        }

        return result;
    }

    private static List<int> ComputeShortestPath(
        int sourceNodeId,
        int targetNodeId,
        GraphSnapshot snapshot,
        CancellationToken ct)
    {
        if (!snapshot.NodeIds.Contains(sourceNodeId))
        {
            throw new InvalidOperationException($"Source node {sourceNodeId} does not exist in graph.");
        }

        if (!snapshot.NodeIds.Contains(targetNodeId))
        {
            throw new InvalidOperationException($"Target node {targetNodeId} does not exist in graph.");
        }

        if (sourceNodeId == targetNodeId)
        {
            return [sourceNodeId];
        }

        var distances = snapshot.NodeIds.ToDictionary(nodeId => nodeId, _ => double.PositiveInfinity);
        var previous = new Dictionary<int, int>();
        distances[sourceNodeId] = 0;

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

            if (currentNodeId == targetNodeId)
            {
                break;
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
                previous[edge.ToNodeId] = currentNodeId;
                queue.Enqueue(edge.ToNodeId, (newDistance, edge.ToNodeId));
            }
        }

        if (double.IsInfinity(distances[targetNodeId]))
        {
            return [];
        }

        var path = new List<int>();
        int cursor = targetNodeId;
        path.Add(cursor);

        while (cursor != sourceNodeId)
        {
            if (!previous.TryGetValue(cursor, out int nextCursor))
            {
                return [];
            }

            cursor = nextCursor;
            path.Add(cursor);
        }

        path.Reverse();
        return path;
    }
}
