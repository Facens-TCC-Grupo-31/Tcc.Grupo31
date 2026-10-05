namespace Application.Services;

public sealed record RoutePlanningResult(
    List<int> NodeVisitOrder,
    List<SelectedCollectionPoint> SelectedSensors,
    double TotalDistance);

public interface IRoutePlanningStrategy
{
    Task<RoutePlanningResult> PlanAsync(
        int startNodeId,
        int endNodeId,
        GraphSnapshot? snapshot = null,
        CancellationToken ct = default
    );
}
