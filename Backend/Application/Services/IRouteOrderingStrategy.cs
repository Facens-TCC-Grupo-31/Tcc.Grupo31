namespace Application.Services;

public interface IRouteOrderingStrategy
{
    IReadOnlyList<int> BuildRoute(
        int depotNodeId,
        IReadOnlyList<int> targetNodeIds,
        Func<int, int, double?> tryGetDistance);
}
