namespace Application.Services;

public sealed record RoutePlanningRequest(
    int DepotNodeId,
    float FillThreshold);

public sealed record RoutePlanningResult(
    IReadOnlyList<int> NodeVisitOrder,
    IReadOnlyList<SelectedCollectionPoint> SelectedSensors,
    double TotalDistance);

public interface IRoutePlanningStrategy
{
    Task<RoutePlanningResult> PlanAsync(RoutePlanningRequest request, CancellationToken ct = default);
}
