namespace Application.Services;

public interface IShortestPathStrategy
{
    Task<Dictionary<int, double>> GetShortestDistancesAsync(
        int sourceNodeId,
        List<int> targetNodeIds,
        CancellationToken ct = default);

    Task<List<int>> GetShortestPathAsync(
        int sourceNodeId,
        int targetNodeId,
        CancellationToken ct = default);

    Task<List<int>> GetShortestPathAsync(
        int sourceNodeId,
        int targetNodeId,
        GraphSnapshot snapshot,
        CancellationToken ct = default);

    Task<Dictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        List<int> waypointNodeIds,
        CancellationToken ct = default);

    Task<Dictionary<(int From, int To), double>> BuildDistanceMatrixAsync(
        List<int> waypointNodeIds,
        GraphSnapshot snapshot,
        CancellationToken ct = default);
}
