using Application.Common.Exceptions;
using Application.Services;

namespace Infrastructure.Services;

internal sealed class NearestNeighborMetricTspOrderingStrategy : IRouteOrderingStrategy
{
    public List<int> BuildRoute(
        int startNodeId,
        int endNodeId,
        List<int> targetNodeIds,
        Func<int, int, double?> tryGetDistance)
    {
        var uniqueTargetNodeIds = targetNodeIds.Distinct()
            .Where(id => id != startNodeId && id != endNodeId)
            .ToList();

        switch (uniqueTargetNodeIds)
        {
            case []:
                return [startNodeId, endNodeId];

            case [var singleNodeId]:
                return [startNodeId, singleNodeId, endNodeId];
        }

        List<int> orderedNodeIds = [];
        var currentNodeId = startNodeId;

        while (uniqueTargetNodeIds.Count > 0)
        {
            int bestNodeId = -1;
            double bestDistance = double.MaxValue;

            foreach (int candidateNodeId in uniqueTargetNodeIds)
            {
                double? maybeDistance = tryGetDistance(currentNodeId, candidateNodeId);
                if (maybeDistance is null)
                {
                    continue;
                }

                double distance = maybeDistance.Value;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestNodeId = candidateNodeId;
                }
            }

            if (bestNodeId < 0)
            {
                throw new UnreachableSelectedBinsException();
            }

            orderedNodeIds.Add(bestNodeId);
            uniqueTargetNodeIds.Remove(bestNodeId);
            currentNodeId = bestNodeId;
        }

        return [startNodeId, ..orderedNodeIds, endNodeId];
    }
}
