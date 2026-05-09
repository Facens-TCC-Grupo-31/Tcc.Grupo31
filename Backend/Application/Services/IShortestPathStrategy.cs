namespace Application.Services;

public interface IShortestPathStrategy
{
    Task<IReadOnlyDictionary<int, double>> GetShortestDistancesAsync(
        int sourceNodeId,
        IReadOnlyCollection<int> targetNodeIds,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        IReadOnlyCollection<int> waypointNodeIds,
        CancellationToken ct = default);
}
