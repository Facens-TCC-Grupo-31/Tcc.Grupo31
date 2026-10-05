using Application.Common.Utils;
using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal sealed class GraphService(
    AppDbContext db,
    ILogger<GraphService> logger,
    ICoordinateDistanceCalculator coordinateDistanceCalculator) : IGraphService
{
    private readonly ICoordinateDistanceCalculator _coordinateDistanceCalculator = coordinateDistanceCalculator;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private static readonly Lock CacheLock = new();

    private static bool _loaded;
    private static readonly Dictionary<int, GraphNode> Nodes = [];
    private static readonly Dictionary<int, GraphEdge> Edges = [];
    private static readonly Dictionary<int, List<GraphEdge>> OutgoingEdgesByNode = [];

    public IDisposable? TryAcquireWriteLock()
    {
        bool acquired = WriteLock.Wait(LockTimeout);
        return acquired ? new LockHandle(WriteLock) : null;
    }

    public async Task<GraphStats> GetGraphStatsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        lock (CacheLock)
        {
            return new GraphStats(Nodes.Count, Edges.Count);
        }
    }

    public async Task<GraphSnapshot> GetGraphSnapshotAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        lock (CacheLock)
        {
            IReadOnlyCollection<int> nodeIds = Nodes.Keys
                .OrderBy(nodeId => nodeId)
                .ToList();

            var adjacency = OutgoingEdgesByNode.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<GraphNeighbor>)kvp.Value
                    .Select(edge => new GraphNeighbor(edge.ToNodeId, edge.Id, edge.Distance))
                    .OrderBy(neighbor => neighbor.ToNodeId)
                    .ThenBy(neighbor => neighbor.EdgeId)
                    .ToList());

            return new GraphSnapshot(nodeIds, adjacency);
        }
    }

    public async Task<GraphEdgeProjection> ProjectOntoNearestEdgeAsync(
        Position position,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (Edges.Count == 0)
        {
            throw new InvalidOperationException("No graph edges available for nearest-edge projection.");
        }

        var (edge, projectedLongitude, projectedLatitude) = FindNearestEdge(position.Longitude, position.Latitude);

        GraphNode fromNode = Nodes[edge.FromNodeId];
        GraphNode toNode = Nodes[edge.ToNodeId];

        bool hasReverseEdge = Edges.Values.Any(
            e => e.FromNodeId == edge.ToNodeId && e.ToNodeId == edge.FromNodeId);

        return new GraphEdgeProjection(
            edge.FromNodeId,
            edge.ToNodeId,
            new Position(projectedLatitude, projectedLongitude),
            _coordinateDistanceCalculator.CalculateDistanceMeters(fromNode.Latitude, fromNode.Longitude, projectedLatitude, projectedLongitude),
            _coordinateDistanceCalculator.CalculateDistanceMeters(toNode.Latitude, toNode.Longitude, projectedLatitude, projectedLongitude),
            hasReverseEdge);
    }

    public async Task<int> ApplyNearestEdgeSplitAsync(
        Position position,
        Func<int, Task> applyMutation,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (Edges.Count == 0)
        {
            logger.LogInformation(
                "Graph has no edges; inserting isolated node at input position lat={Latitude}, lon={Longitude}",
                position.Latitude,
                position.Longitude);

            return await InsertIsolatedNodeAsync(position.Longitude, position.Latitude, applyMutation, ct);
        }

        var (edge, projectedLongitude, projectedLatitude) = FindNearestEdge(position.Longitude, position.Latitude);
        GraphNode nearestFrom = Nodes[edge.FromNodeId];
        GraphNode nearestTo = Nodes[edge.ToNodeId];
        double projectionDistance = _coordinateDistanceCalculator.CalculateDistanceMeters(position.Latitude, position.Longitude, projectedLatitude, projectedLongitude);

        logger.LogInformation(
            "Graph split input lat={Latitude}, lon={Longitude}; nearest edge {EdgeId} from (lat={FromLat}, lon={FromLon}) to (lat={ToLat}, lon={ToLon}); projected to (lat={ProjectedLat}, lon={ProjectedLon}) with distance {ProjectionDistance}",
            position.Latitude,
            position.Longitude,
            edge.Id,
            nearestFrom.Latitude,
            nearestFrom.Longitude,
            nearestTo.Latitude,
            nearestTo.Longitude,
            projectedLatitude,
            projectedLongitude,
            projectionDistance);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        GraphEdge? edgeEntity = await db.GraphEdges
            .SingleOrDefaultAsync(e => e.Id == edge.Id, ct);
        if (edgeEntity is null)
            throw new InvalidOperationException($"Nearest edge {edge.Id} no longer exists.");

        GraphEdge? reverseEdgeEntity = await db.GraphEdges
            .SingleOrDefaultAsync(
                e => e.FromNodeId == edge.ToNodeId && e.ToNodeId == edge.FromNodeId,
                ct);

        GraphNode fromNode = Nodes[edge.FromNodeId];
        GraphNode toNode = Nodes[edge.ToNodeId];

        var newNode = new GraphNode { Longitude = projectedLongitude, Latitude = projectedLatitude };
        db.GraphNodes.Add(newNode);
        await db.SaveChangesAsync(ct);

        var edge1 = new GraphEdge
        {
            FromNodeId = fromNode.Id,
            ToNodeId = newNode.Id,
            Distance = _coordinateDistanceCalculator.CalculateDistanceMeters(fromNode.Latitude, fromNode.Longitude, newNode.Latitude, newNode.Longitude)
        };
        var edge2 = new GraphEdge
        {
            FromNodeId = newNode.Id,
            ToNodeId = toNode.Id,
            Distance = _coordinateDistanceCalculator.CalculateDistanceMeters(newNode.Latitude, newNode.Longitude, toNode.Latitude, toNode.Longitude)
        };

        var newEdges = new List<GraphEdge> { edge1, edge2 };
        var edgesToRemove = new List<GraphEdge> { edgeEntity };

        if (reverseEdgeEntity is not null)
        {
            var edge3 = new GraphEdge
            {
                FromNodeId = toNode.Id,
                ToNodeId = newNode.Id,
                Distance = _coordinateDistanceCalculator.CalculateDistanceMeters(toNode.Latitude, toNode.Longitude, newNode.Latitude, newNode.Longitude)
            };

            var edge4 = new GraphEdge
            {
                FromNodeId = newNode.Id,
                ToNodeId = fromNode.Id,
                Distance = _coordinateDistanceCalculator.CalculateDistanceMeters(newNode.Latitude, newNode.Longitude, fromNode.Latitude, fromNode.Longitude)
            };

            newEdges.Add(edge3);
            newEdges.Add(edge4);
            edgesToRemove.Add(reverseEdgeEntity);
        }

        db.GraphEdges.RemoveRange(edgesToRemove);
        db.GraphEdges.AddRange(newEdges);
        await db.SaveChangesAsync(ct);

        await applyMutation(newNode.Id);

        await tx.CommitAsync(ct);

        lock (CacheLock)
        {
            Nodes[newNode.Id] = newNode;

            foreach (GraphEdge edgeToRemove in edgesToRemove)
            {
                Edges.Remove(edgeToRemove.Id);
                RemoveEdgeFromAdjacency(edgeToRemove);
            }

            foreach (GraphEdge newEdge in newEdges)
            {
                Edges[newEdge.Id] = newEdge;
                AddEdgeToAdjacency(newEdge);
            }
        }

        logger.LogInformation(
            "Graph edge {EdgeId} split by new node {NodeId} at (lat={Latitude}, lon={Longitude})",
            edge.Id,
            newNode.Id,
            newNode.Latitude,
            newNode.Longitude
        );

        return newNode.Id;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            bool cacheValid = await IsCacheStillValidAsync(ct);
            if (cacheValid)
            {
                return;
            }

            lock (CacheLock)
            {
                _loaded = false;
            }
        }

        List<GraphNode> nodes = await db.GraphNodes.AsNoTracking().ToListAsync(ct);
        List<GraphEdge> edges = await db.GraphEdges.AsNoTracking().ToListAsync(ct);

        lock (CacheLock)
        {
            if (_loaded)
                return;

            Nodes.Clear();
            Edges.Clear();
            OutgoingEdgesByNode.Clear();

            foreach (GraphNode node in nodes)
                Nodes[node.Id] = node;
            foreach (GraphEdge edge in edges)
            {
                Edges[edge.Id] = edge;
                AddEdgeToAdjacency(edge);
            }

            _loaded = true;
        }
    }

    private async Task<bool> IsCacheStillValidAsync(CancellationToken ct)
    {
        int dbNodeCount = await db.GraphNodes.AsNoTracking().CountAsync(ct);
        int dbEdgeCount = await db.GraphEdges.AsNoTracking().CountAsync(ct);

        int dbMaxNodeId = await db.GraphNodes
            .AsNoTracking()
            .Select(n => (int?)n.Id)
            .MaxAsync(ct) ?? 0;

        int dbMaxEdgeId = await db.GraphEdges
            .AsNoTracking()
            .Select(e => (int?)e.Id)
            .MaxAsync(ct) ?? 0;

        lock (CacheLock)
        {
            int cacheNodeCount = Nodes.Count;
            int cacheEdgeCount = Edges.Count;
            int cacheMaxNodeId = cacheNodeCount == 0 ? 0 : Nodes.Keys.Max();
            int cacheMaxEdgeId = cacheEdgeCount == 0 ? 0 : Edges.Keys.Max();

            return cacheNodeCount == dbNodeCount
                && cacheEdgeCount == dbEdgeCount
                && cacheMaxNodeId == dbMaxNodeId
                && cacheMaxEdgeId == dbMaxEdgeId;
        }
    }

    private async Task<int> InsertIsolatedNodeAsync(
        double longitude,
        double latitude,
        Func<int, Task> applyMutation,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var node = new GraphNode { Longitude = longitude, Latitude = latitude };
        db.GraphNodes.Add(node);
        await db.SaveChangesAsync(ct);

        await applyMutation(node.Id);
        await tx.CommitAsync(ct);

        lock (CacheLock)
        {
            Nodes[node.Id] = node;
            OutgoingEdgesByNode.TryAdd(node.Id, []);
        }

        logger.LogInformation("Graph initialized with first node {NodeId}", node.Id);
        return node.Id;
    }

    private (GraphEdge edge, double projectedLongitude, double projectedLatitude) FindNearestEdge(double longitude, double latitude)
    {
        GraphEdge? nearest = null;
        double nearestDistanceSquared = double.MaxValue;
        double nearestProjectedLongitude = 0;
        double nearestProjectedLatitude = 0;

        foreach (GraphEdge edge in Edges.Values)
        {
            GraphNode fromNode = Nodes[edge.FromNodeId];
            GraphNode toNode = Nodes[edge.ToNodeId];

            (double projectedLongitude, double projectedLatitude) = ProjectPointOntoSegment(
                longitude,
                latitude,
                fromNode.Longitude,
                fromNode.Latitude,
                toNode.Longitude,
                toNode.Latitude);

            double distanceSquared = _coordinateDistanceCalculator.CalculateSquaredDistanceMeters(latitude, longitude, projectedLatitude, projectedLongitude);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = edge;
                nearestProjectedLongitude = projectedLongitude;
                nearestProjectedLatitude = projectedLatitude;
            }
        }

        if (nearest is null)
            throw new InvalidOperationException("No graph edges available for nearest-edge split.");

        return (nearest, nearestProjectedLongitude, nearestProjectedLatitude);
    }

    private static (double longitude, double latitude) ProjectPointOntoSegment(
        double pointLongitude,
        double pointLatitude,
        double fromLongitude,
        double fromLatitude,
        double toLongitude,
        double toLatitude)
    {
        double deltaLongitude = toLongitude - fromLongitude;
        double deltaLatitude = toLatitude - fromLatitude;
        double segmentMagnitudeSquared = deltaLongitude * deltaLongitude + deltaLatitude * deltaLatitude;
        if (segmentMagnitudeSquared == 0)
            return (fromLongitude, fromLatitude);

        double pointOffsetLongitude = pointLongitude - fromLongitude;
        double pointOffsetLatitude = pointLatitude - fromLatitude;
        double t = (pointOffsetLongitude * deltaLongitude + pointOffsetLatitude * deltaLatitude) / segmentMagnitudeSquared;
        t = Math.Clamp(t, 0, 1);
        return (fromLongitude + t * deltaLongitude, fromLatitude + t * deltaLatitude);
    }

    private static void AddEdgeToAdjacency(GraphEdge edge)
    {
        if (!OutgoingEdgesByNode.TryGetValue(edge.FromNodeId, out List<GraphEdge>? edges))
        {
            edges = [];
            OutgoingEdgesByNode[edge.FromNodeId] = edges;
        }

        edges.Add(edge);
        edges.Sort((a, b) =>
        {
            int toCompare = a.ToNodeId.CompareTo(b.ToNodeId);
            return toCompare != 0 ? toCompare : a.Id.CompareTo(b.Id);
        });
    }

    private static void RemoveEdgeFromAdjacency(GraphEdge edge)
    {
        if (!OutgoingEdgesByNode.TryGetValue(edge.FromNodeId, out List<GraphEdge>? edges))
        {
            return;
        }

        edges.RemoveAll(e => e.Id == edge.Id);
    }

    private sealed class LockHandle(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
