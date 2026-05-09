using Application.Services;

namespace Infrastructure.Services;

internal sealed class NearestNeighborMetricTspOrderingStrategy : IRouteOrderingStrategy
{
    public IReadOnlyList<int> BuildRoute(
        int depotNodeId,
        IReadOnlyList<int> targetNodeIds,
        Func<int, int, double?> tryGetDistance)
    {
        var uniqueTargets = targetNodeIds
            .Where(nodeId => nodeId != depotNodeId)
            .Distinct()
            .OrderBy(nodeId => nodeId)
            .ToList();

        var route = new List<int>(uniqueTargets.Count + 2) { depotNodeId };
        if (uniqueTargets.Count == 0)
        {
            route.Add(depotNodeId);
            return route;
        }

        int current = depotNodeId;

        while (uniqueTargets.Count > 0)
        {
            int bestNode = -1;
            double bestDistance = double.MaxValue;

            foreach (int candidate in uniqueTargets)
            {
                double? maybeDistance = tryGetDistance(current, candidate);
                if (!maybeDistance.HasValue)
                {
                    continue;
                }

                double distance = maybeDistance.Value;
                if (distance < bestDistance || (distance == bestDistance && candidate < bestNode))
                {
                    bestDistance = distance;
                    bestNode = candidate;
                }
            }

            if (bestNode < 0)
            {
                throw new InvalidOperationException("Unable to build route: graph contains unreachable selected bins.");
            }

            route.Add(bestNode);
            uniqueTargets.Remove(bestNode);
            current = bestNode;
        }

        route.Add(depotNodeId);
        return route;
    }
}