using Application.Cache;
using Application.Common.Dtos;
using Application.Services;
using Domain.Entities;
using Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal sealed class SensorRegistrationService(
    AppDbContext db,
    IProvisioningDataCache provisioningDataCache,
    IGraphService graphService,
    ILogger<SensorRegistrationService> logger) : ISensorRegistrationService
{
    public async Task<RegistrationResponseDto> RequestRegistrationAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        var sensor = new Sensor
        {
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Sensors.Add(sensor);
        await db.SaveChangesAsync(ct);

        string token = Guid.NewGuid().ToString();
        await provisioningDataCache.SetAsync(
            sensor.Id,
            new ProvisioningRegistrationContext(token, latitude, longitude),
            ct);

        logger.LogInformation(
            "Registration requested for sensor {SensorId} at ({Lat},{Lon})",
            sensor.Id,
            latitude,
            longitude
        );

        return new RegistrationResponseDto
        {
            SensorNodeId = sensor.Id,
            ProvisioningToken = token
        };
    }

    public async Task<bool> CompleteRegistrationAsync(
        long sensorId,
        string token,
        CancellationToken ct = default
    )
    {
        using IDisposable? writeLock = graphService.TryAcquireWriteLock();
        if (writeLock is null)
        {
            logger.LogWarning(
                "Graph write lock timed out during activation of sensor {SensorId}", sensorId);
            return false;
        }

        Sensor? sensor = await db.Sensors.FindAsync([sensorId], ct);

        if (sensor is null)
        {
            logger.LogWarning("Sensor {SensorId} not found during registration completion", sensorId);
            return false;
        }

        if (sensor.IsActive)
        {
            logger.LogWarning("Sensor {SensorId} is already active; ignoring completion attempt", sensorId);
            return false;
        }

        ProvisioningRegistrationContext? registrationContext = await provisioningDataCache.ConsumeAsync(sensorId, ct);
        if (registrationContext is null || registrationContext.Token != token)
        {
            logger.LogWarning(
                "Invalid or expired token for sensor {SensorId}", sensorId);
            return false;
        }

        try
        {
            await graphService.ApplyNearestEdgeSplitAsync(
                registrationContext.Latitude,
                registrationContext.Longitude,
                async newNodeId =>
                {
                    sensor.IsActive = true;
                    sensor.NodeId = newNodeId;
                    sensor.ActivatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                },
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "DB failure during activation of sensor {SensorId}; token already consumed",
                sensorId
            );

            return false;
        }

        logger.LogInformation(
            "Sensor {SensorId} activated as node {NodeId}",
            sensorId,
            sensor.NodeId
        );

        return true;
    }
}


