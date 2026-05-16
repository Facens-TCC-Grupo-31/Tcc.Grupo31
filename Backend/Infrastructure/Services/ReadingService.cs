using Application.Cache;
using Application.Common.Dtos;
using Application.Services;
using Domain.Entities;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal sealed class ReadingService(
    AppDbContext db,
    ISensorLatestValueCache cache,
    ILogger<ReadingService> logger) : IReadingService
{
    private const int MaxDistanceMm = 100_000;

    public async Task<bool> RegisterReadingAsync(
        long sensorId, int distanceMm, CancellationToken ct = default)
    {
        if (distanceMm <= 0 || distanceMm > MaxDistanceMm)
        {
            logger.LogWarning(
                "Rejected sample for sensor {SensorId}: distance {DistanceMm} out of range (1..{MaxDistanceMm}]",
                sensorId,
                distanceMm,
                MaxDistanceMm);
            return false;
        }

        Sensor? sensor = await db.Sensors
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sensorId, ct);

        if (sensor is null || !sensor.IsActive)
        {
            logger.LogWarning(
                "Rejected sample for sensor {SensorId}: sensor missing or inactive",
                sensorId);
            return false;
        }

        if (!sensor.EmptyDistanceMm.HasValue || sensor.EmptyDistanceMm.Value <= 0)
        {
            logger.LogWarning(
                "Rejected sample for sensor {SensorId}: sensor has no calibration baseline",
                sensorId);
            return false;
        }

        float fillLevel = 1f - ((float)distanceMm / sensor.EmptyDistanceMm.Value);
        fillLevel = Math.Clamp(fillLevel, 0f, 1f);

        DateTime now = DateTime.UtcNow;

        var rawMeasurement = new SensorDistanceMeasurement
        {
            SensorId = sensorId,
            DistanceMm = distanceMm,
            ReceivedAtUtc = now,
            Source = "mqtt"
        };

        var reading = new SensorReading
        {
            SensorId = sensorId,
            FillLevel = fillLevel,
            Timestamp = now
        };

        db.SensorDistanceMeasurements.Add(rawMeasurement);
        db.SensorReadings.Add(reading);
        await db.SaveChangesAsync(ct);

        await cache.SetAsync(sensorId, fillLevel, now, ct);

        return true;
    }

    public async Task<IReadOnlyList<SensorReading>> GetReadingsAsync(
        long sensorId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        IQueryable<SensorReading> q = db.SensorReadings
            .AsNoTracking()
            .Where(r => r.SensorId == sensorId);

        if (from.HasValue)
            q = q.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            q = q.Where(r => r.Timestamp <= to.Value);

        return await q
            .OrderByDescending(r => r.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<ReadingDto?> GetLatestAsync(long sensorId, CancellationToken ct = default)
    {
        SensorLatestValue? latest = await cache.GetAsync(sensorId, ct);
        if (latest is null)
            return null;

        return new ReadingDto
        {
            SensorId = sensorId,
            FillLevel = latest.FillLevel
        };
    }
}
