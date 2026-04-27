using Application.Persistence;
using Application.Persistence.Entities;
using Application.Common.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Services.Abstractions;
using Application.Infrastructure.Abstractions;

namespace Application.Services;

internal sealed class ReadingService(
    AppDbContext db,
    ISensorLatestValueCache cache,
    ILogger<ReadingService> logger) : IReadingService
{
    public async Task<bool> RegisterReadingAsync(
        long sensorId, float fillLevel, CancellationToken ct = default)
    {
        if (fillLevel < 0f || fillLevel > 1f)
        {
            logger.LogWarning(
                "Rejected sample for sensor {SensorId}: fill level {FillLevel} out of range [0,1]",
                sensorId, fillLevel);
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

        DateTime now = DateTime.UtcNow;
        var reading = new SensorReading
        {
            SensorId = sensorId,
            FillLevel = fillLevel,
            Timestamp = now
        };

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
            FillLevel = latest.FillLevel,
            Timestamp = latest.Timestamp
        };
    }
}
