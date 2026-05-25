using Application.Cache;
using Application.Common.Dtos;
using Application.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

internal sealed class SensorQueryService(
    AppDbContext db,
    ISensorLatestValueCache latestValueCache,
    ISensorLivenessCache livenessCache) : ISensorQueryService
{
    public async Task<SensorNodePagedResponseDto> GetPagedAsync(
        int currentPageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        int totalCount = await db.Sensors.AsNoTracking().CountAsync(ct);

        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        int skip = (currentPageNumber - 1) * pageSize;

        var sensors = await db.Sensors
            .AsNoTracking()
            .OrderBy(sensor => sensor.Id)
            .Select(sensor => new
            {
                sensor.Id,
                sensor.IsActive,
                sensor.NodeId,
                sensor.CreatedAt,
                sensor.ActivatedAt,
                sensor.EmptyDistanceMm,
                sensor.CalibratedAtUtc,
                sensor.CalibrationSampleCount,
                sensor.CalibrationMethod,
                NodeLatitude = sensor.Node != null ? (double?)sensor.Node.Latitude : null,
                NodeLongitude = sensor.Node != null ? (double?)sensor.Node.Longitude : null
            })
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<SensorNodeDetailDto>(sensors.Count);

        foreach (var sensor in sensors)
        {
            items.Add(await BuildDetailAsync(
                sensor.Id,
                sensor.IsActive,
                sensor.NodeId,
                sensor.CreatedAt,
                sensor.ActivatedAt,
                sensor.EmptyDistanceMm,
                sensor.CalibratedAtUtc,
                sensor.CalibrationSampleCount,
                sensor.CalibrationMethod,
                sensor.NodeLatitude,
                sensor.NodeLongitude,
                ct));
        }

        return new SensorNodePagedResponseDto
        {
            CurrentPageNumber = currentPageNumber,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalCount = totalCount,
            Items = items
        };
    }

    public async Task<SensorNodeDetailDto?> GetByIdAsync(long sensorId, CancellationToken ct = default)
    {
        var sensor = await db.Sensors
            .AsNoTracking()
            .Where(item => item.Id == sensorId)
            .Select(item => new
            {
                item.Id,
                item.IsActive,
                item.NodeId,
                item.CreatedAt,
                item.ActivatedAt,
                item.EmptyDistanceMm,
                item.CalibratedAtUtc,
                item.CalibrationSampleCount,
                item.CalibrationMethod,
                NodeLatitude = item.Node != null ? (double?)item.Node.Latitude : null,
                NodeLongitude = item.Node != null ? (double?)item.Node.Longitude : null
            })
            .SingleOrDefaultAsync(ct);

        if (sensor is null)
        {
            return null;
        }

        return await BuildDetailAsync(
            sensor.Id,
            sensor.IsActive,
            sensor.NodeId,
            sensor.CreatedAt,
            sensor.ActivatedAt,
            sensor.EmptyDistanceMm,
            sensor.CalibratedAtUtc,
            sensor.CalibrationSampleCount,
            sensor.CalibrationMethod,
            sensor.NodeLatitude,
            sensor.NodeLongitude,
            ct);
    }

    private async Task<SensorNodeDetailDto> BuildDetailAsync(
        long sensorId,
        bool isActive,
        int? nodeId,
        DateTime createdAt,
        DateTime? activatedAt,
        int? emptyDistanceMm,
        DateTime? calibratedAtUtc,
        short? calibrationSampleCount,
        string? calibrationMethod,
        double? nodeLatitude,
        double? nodeLongitude,
        CancellationToken ct)
    {
        SensorLatestValue? latest = await latestValueCache.GetAsync(sensorId, ct);
        bool isAlive = await livenessCache.IsAliveAsync(sensorId, ct);

        string healthState = !isActive
            ? "inactive"
            : isAlive
                ? "alive"
                : latest is null
                    ? "unknown"
                    : "stale";

        return new SensorNodeDetailDto
        {
            Id = sensorId,
            IsActive = isActive,
            NodeId = nodeId,
            CreatedAt = createdAt,
            ActivatedAt = activatedAt,
            EmptyDistanceMm = emptyDistanceMm,
            CalibratedAtUtc = calibratedAtUtc,
            CalibrationSampleCount = calibrationSampleCount,
            CalibrationMethod = calibrationMethod,
            NodeLatitude = nodeLatitude,
            NodeLongitude = nodeLongitude,
            Health = new SensorNodeHealthDto
            {
                IsAlive = isAlive,
                HealthState = healthState,
                LatestFillLevel = latest?.FillLevel,
                LatestReadingTimestamp = latest?.Timestamp
            }
        };
    }
}
