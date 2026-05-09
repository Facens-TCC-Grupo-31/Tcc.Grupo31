namespace Application.Services;

public sealed record SelectedCollectionPoint(long SensorId, int NodeId, float FillLevel);

public interface IPointSelectionStrategy
{
    Task<IReadOnlyList<SelectedCollectionPoint>> SelectPointsAsync(float threshold, CancellationToken ct = default);
}
