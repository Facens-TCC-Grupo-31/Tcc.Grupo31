namespace Application.Services;

public interface IRouteOrderingStrategy
{
    List<int> BuildRoute(
        int startNodeId,
        int endNodeId,
        List<int> targetNodeIds,
        Func<int, int, double?> tryGetDistance
    );
}
