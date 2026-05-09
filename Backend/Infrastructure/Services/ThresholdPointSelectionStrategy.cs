using Application.Cache;
using Application.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

internal sealed class ThresholdPointSelectionStrategy(
    AppDbContext db,
    ISensorLatestValueCache latestValueCache) : IPointSelectionStrategy
{
    public async Task<IReadOnlyList<SelectedCollectionPoint>> SelectPointsAsync(float threshold, CancellationToken ct = default)
    {
        var sensors = await db.Sensors
            .AsNoTracking()
            .Where(s => s.IsActive && s.NodeId.HasValue)
            .Select(s => new { s.Id, NodeId = s.NodeId!.Value })
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        IReadOnlyDictionary<long, SensorLatestValue> latest = await latestValueCache.GetAllAsync(ct);
        var result = new List<SelectedCollectionPoint>(sensors.Count);

        foreach (var sensor in sensors)
        {
            float fillLevel = latest.TryGetValue(sensor.Id, out SensorLatestValue? value)
                ? value.FillLevel
                : 0f;

            if (fillLevel < threshold)
            {
                continue;
            }

            result.Add(new SelectedCollectionPoint(sensor.Id, sensor.NodeId, fillLevel));
        }

        return result;
    }
}
