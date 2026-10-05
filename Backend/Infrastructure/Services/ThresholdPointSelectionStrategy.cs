using Application.Cache;
using Application.Services;
using Infrastructure.Database;
using Infrastructure.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

internal sealed class ThresholdPointSelectionStrategy(
    AppDbContext db,
    ISensorLatestValueCache latestValueCache,
    IOptions<RoutingOptions> options) : IPointSelectionStrategy
{
    private readonly double _threshold = options.Value.FillThreshold;

    public async Task<IReadOnlyList<SelectedCollectionPoint>> SelectPointsAsync(CancellationToken ct = default)
    {
        var sensors = await db.Sensors
            .AsNoTracking()
            .Where(s => s.IsActive && s.NodeId.HasValue)
            .Select(s => new { s.Id, NodeId = s.NodeId!.Value })
            .ToListAsync(ct);

        var sensorLatestValues = await latestValueCache.GetAllAsync(ct);

        var result = new List<SelectedCollectionPoint>();

        foreach (var sensor in sensors)
        {
            if (!sensorLatestValues.TryGetValue(sensor.Id, out var value) || value.FillLevel < _threshold)
            {
                continue;
            }

            result.Add(new SelectedCollectionPoint(sensor.Id, sensor.NodeId, value.FillLevel, value.Timestamp));
        }

        return result;
    }
}
