using Domain.ValueObjects;

namespace Application.Services;

public interface IGraphService
{
    IDisposable? TryAcquireWriteLock();

    Task<GraphStats> GetGraphStatsAsync(CancellationToken ct = default);

    Task<GraphSnapshot> GetGraphSnapshotAsync(CancellationToken ct = default);

    Task<int> ApplyNearestEdgeSplitAsync(
        Position position,
        Func<int, Task> applyMutation,
        CancellationToken ct = default
    );
}

public sealed record GraphStats(int NodeCount, int EdgeCount);

public sealed record GraphNeighbor(int ToNodeId, int EdgeId, double Distance);

public sealed record GraphSnapshot(
    IReadOnlyCollection<int> NodeIds,
    IReadOnlyDictionary<int, IReadOnlyList<GraphNeighbor>> AdjacencyByNode);
