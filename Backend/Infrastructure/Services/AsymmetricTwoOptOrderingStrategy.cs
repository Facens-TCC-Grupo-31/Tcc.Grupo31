using Application.Services;

namespace Infrastructure.Services;

internal sealed class AsymmetricTwoOptOrderingStrategy : IRouteOrderingStrategy
{
    private readonly NearestNeighborMetricTspOrderingStrategy _initialOrderingStrategy = new();

    public List<int> BuildRoute(
        int startNodeId,
        int endNodeId,
        List<int> targetNodeIds,
        Func<int, int, double?> tryGetDistance)
    {
        List<int> route = _initialOrderingStrategy.BuildRoute(
            startNodeId,
            endNodeId,
            targetNodeIds,
            tryGetDistance);

        if (route.Count <= 3)
        {
            return route;
        }

        bool improved;

        do
        {
            improved = false;

            for (int i = 1; i < route.Count - 2; i++)
            {
                for (int j = i + 1; j < route.Count - 1; j++)
                {
                    double? delta = TryGetTwoOptDelta(
                        route,
                        i,
                        j,
                        tryGetDistance);

                    if (delta is null || delta.Value >= 0)
                    {
                        continue;
                    }

                    ReverseSegment(route, i, j);

                    improved = true;
                    goto NextIteration;
                }
            }

        NextIteration:;
        }
        while (improved);

        return route;
    }

    private static double? TryGetTwoOptDelta(
        List<int> route,
        int startIndex,
        int endIndex,
        Func<int, int, double?> tryGetDistance)
    {
        int beforeStartNodeId = route[startIndex - 1];
        int startNodeId = route[startIndex];

        int endNodeId = route[endIndex];
        int afterEndNodeId = route[endIndex + 1];

        double? oldStartEdge = tryGetDistance(
            beforeStartNodeId,
            startNodeId);

        double? newStartEdge = tryGetDistance(
            beforeStartNodeId,
            endNodeId);

        double? oldEndEdge = tryGetDistance(
            endNodeId,
            afterEndNodeId);

        double? newEndEdge = tryGetDistance(
            startNodeId,
            afterEndNodeId);

        if (oldStartEdge is null ||
            newStartEdge is null ||
            oldEndEdge is null ||
            newEndEdge is null)
        {
            return null;
        }

        double delta =
            newStartEdge.Value
            + newEndEdge.Value
            - oldStartEdge.Value
            - oldEndEdge.Value;

        for (int index = startIndex; index < endIndex; index++)
        {
            int fromNodeId = route[index];
            int toNodeId = route[index + 1];

            double? oldEdge = tryGetDistance(
                fromNodeId,
                toNodeId);

            double? reversedEdge = tryGetDistance(
                toNodeId,
                fromNodeId);

            if (oldEdge is null || reversedEdge is null)
            {
                return null;
            }

            delta += reversedEdge.Value - oldEdge.Value;
        }

        return delta;
    }

    private static void ReverseSegment(
        List<int> route,
        int startIndex,
        int endIndex)
    {
        route.Reverse(startIndex, endIndex - startIndex + 1);
    }
}