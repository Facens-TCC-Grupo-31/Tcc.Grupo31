namespace Application.Services;

public sealed record SelectedCollectionPoint(
    long SensorId,
    int NodeId,
    float FillLevel,
    DateTime FillTimestamp);

public interface IPointSelectionStrategy
{
    Task<IReadOnlyList<SelectedCollectionPoint>> SelectPointsAsync(CancellationToken ct = default);
}
