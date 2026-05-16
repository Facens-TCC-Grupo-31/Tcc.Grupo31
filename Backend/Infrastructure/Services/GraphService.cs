using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal sealed class GraphService(
    AppDbContext db,
    ILogger<GraphService> logger) : IGraphService
{
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

    public async Task<int> ApplyNearestEdgeSplitAsync(
        Position position,
        Func<int, Task> applyMutation,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (Edges.Count == 0)
            return await InsertIsolatedNodeAsync(position.Longitude, position.Latitude, applyMutation, ct);

        var (edge, px, py) = FindNearestEdge(position.Longitude, position.Latitude);

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

        var newNode = new GraphNode { X = px, Y = py };
        db.GraphNodes.Add(newNode);
        await db.SaveChangesAsync(ct);

        var edge1 = new GraphEdge
        {
            FromNodeId = fromNode.Id,
            ToNodeId = newNode.Id,
            Distance = Distance(fromNode.X, fromNode.Y, newNode.X, newNode.Y)
        };
        var edge2 = new GraphEdge
        {
            FromNodeId = newNode.Id,
            ToNodeId = toNode.Id,
            Distance = Distance(newNode.X, newNode.Y, toNode.X, toNode.Y)
        };

        var newEdges = new List<GraphEdge> { edge1, edge2 };
        var edgesToRemove = new List<GraphEdge> { edgeEntity };

        if (reverseEdgeEntity is not null)
        {
            var edge3 = new GraphEdge
            {
                FromNodeId = toNode.Id,
                ToNodeId = newNode.Id,
                Distance = Distance(toNode.X, toNode.Y, newNode.X, newNode.Y)
            };

            var edge4 = new GraphEdge
            {
                FromNodeId = newNode.Id,
                ToNodeId = fromNode.Id,
                Distance = Distance(newNode.X, newNode.Y, fromNode.X, fromNode.Y)
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
            "Graph edge {EdgeId} split by new node {NodeId} at ({X},{Y})",
            edge.Id,
            newNode.Id,
            newNode.X,
            newNode.Y
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
        double x,
        double y,
        Func<int, Task> applyMutation,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var node = new GraphNode { X = x, Y = y };
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

    private static (GraphEdge edge, double px, double py) FindNearestEdge(double x, double y)
    {
        GraphEdge? nearest = null;
        double nearestDistSq = double.MaxValue;
        double bestPx = 0;
        double bestPy = 0;

        foreach (GraphEdge edge in Edges.Values)
        {
            GraphNode a = Nodes[edge.FromNodeId];
            GraphNode b = Nodes[edge.ToNodeId];

            (double px, double py) = ProjectPointOntoSegment(x, y, a.X, a.Y, b.X, b.Y);
            double d2 = DistSq(x, y, px, py);
            if (d2 < nearestDistSq)
            {
                nearestDistSq = d2;
                nearest = edge;
                bestPx = px;
                bestPy = py;
            }
        }

        if (nearest is null)
            throw new InvalidOperationException("No graph edges available for nearest-edge split.");

        return (nearest, bestPx, bestPy);
    }

    private static (double x, double y) ProjectPointOntoSegment(
        double px,
        double py,
        double ax,
        double ay,
        double bx,
        double by)
    {
        double abx = bx - ax;
        double aby = by - ay;
        double ab2 = abx * abx + aby * aby;
        if (ab2 == 0)
            return (ax, ay);

        double apx = px - ax;
        double apy = py - ay;
        double t = (apx * abx + apy * aby) / ab2;
        t = Math.Clamp(t, 0, 1);
        return (ax + t * abx, ay + t * aby);
    }

    private static double DistSq(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2;
        double dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
        => Math.Sqrt(DistSq(x1, y1, x2, y2));

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
