namespace Application.Services;

public sealed record RoutePlanningRequest(
    int DepotNodeId,
    float FillThreshold);

public sealed record RoutePlanningResult(
    IReadOnlyList<int> NodeVisitOrder,
    IReadOnlyList<long> SensorVisitOrder,
    int SelectedBins,
    int VisitedBins,
    double TotalDistance,
    double AverageShortestPathCost,
    double MatrixGenerationMs);

public interface IRoutePlanningStrategy
{
    Task<RoutePlanningResult> PlanAsync(RoutePlanningRequest request, CancellationToken ct = default);
}
