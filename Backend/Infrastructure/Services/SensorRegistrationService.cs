using Application.Cache;
using Application.Common.Dtos;
using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

internal sealed class SensorRegistrationService(
    AppDbContext db,
    IProvisioningDataCache provisioningDataCache,
    IGraphService graphService,
    ILogger<SensorRegistrationService> logger,
    IMockSensorRuntimeNotifier? mockSensorRuntimeNotifier = null) : ISensorRegistrationService
{
    private const string MockCalibrationMethod = "mock";

    public async Task<RegistrationResponseDto> RequestRegistrationAsync(
        Position position,
        CancellationToken ct = default)
    {
        var sensor = new Sensor
        {
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Sensors.Add(sensor);
        await db.SaveChangesAsync(ct);

        string token = "test";
        await provisioningDataCache.SetAsync(
            sensor.Id,
            new ProvisioningRegistrationContext(token, position),
            ct);

        logger.LogInformation(
            "Registration requested for sensor {SensorId} at (lat={Latitude}, lon={Longitude})",
            sensor.Id,
            position.Latitude,
            position.Longitude
        );

        return new RegistrationResponseDto
        {
            SensorId = sensor.Id,
            ProvisioningToken = token
        };
    }

    public async Task<RegistrationResponseDto> RequestMockRegistrationAsync(
        Position position,
        int baselineDistanceMm,
        int desiredReadingMm,
        CancellationToken ct = default)
    {
        if (baselineDistanceMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baselineDistanceMm),
                "Mock baseline must be greater than zero.");
        }

        if (desiredReadingMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredReadingMm),
                "Mock desired reading must be greater than zero.");
        }

        if (desiredReadingMm > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredReadingMm),
                $"Mock desired reading must be <= {short.MaxValue}.");
        }

        using IDisposable? writeLock = graphService.TryAcquireWriteLock();
        if (writeLock is null)
        {
            throw new InvalidOperationException("Graph write lock timed out during mock registration.");
        }

        var sensor = new Sensor
        {
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Sensors.Add(sensor);
        await db.SaveChangesAsync(ct);

        await graphService.ApplyNearestEdgeSplitAsync(
            position,
            async newNodeId =>
            {
                sensor.IsActive = true;
                sensor.NodeId = newNodeId;
                sensor.ActivatedAt = DateTime.UtcNow;
                sensor.EmptyDistanceMm = baselineDistanceMm;
                sensor.CalibratedAtUtc = DateTime.UtcNow;
                sensor.CalibrationSampleCount = (short)desiredReadingMm;
                sensor.CalibrationMethod = MockCalibrationMethod;

                await db.SaveChangesAsync(ct);
            },
            ct
        );

        GraphNode? activatedNode = await db.GraphNodes
            .AsNoTracking()
            .SingleOrDefaultAsync(node => node.Id == sensor.NodeId, ct);

        logger.LogInformation(
            "Mock sensor {SensorId} activated as node {NodeId} from input (lat={InputLatitude}, lon={InputLongitude}) snapped to (lat={SnappedLatitude}, lon={SnappedLongitude}) with baseline {BaselineDistanceMm} and desired reading {DesiredReadingMm}",
            sensor.Id,
            sensor.NodeId,
            position.Latitude,
            position.Longitude,
            activatedNode?.Latitude,
            activatedNode?.Longitude,
            baselineDistanceMm,
            desiredReadingMm
        );

        if (mockSensorRuntimeNotifier is not null)
        {
            await mockSensorRuntimeNotifier.StartMockSensorAsync(
                sensor.Id,
                baselineDistanceMm,
                desiredReadingMm,
                ct
            );
        }

        return new RegistrationResponseDto
        {
            SensorId = sensor.Id,
            ProvisioningToken = string.Empty
        };
    }

    public async Task<bool> CompleteRegistrationAsync(
        long sensorId,
        string token,
        int baselineDistanceMm,
        short calibrationSampleCount,
        CancellationToken ct = default
    )
    {
        if (baselineDistanceMm <= 0)
        {
            logger.LogWarning(
                "Invalid baseline distance for sensor {SensorId}: {BaselineDistanceMm}",
                sensorId,
                baselineDistanceMm);
            return false;
        }

        if (calibrationSampleCount <= 0)
        {
            logger.LogWarning(
                "Invalid calibration sample count for sensor {SensorId}: {CalibrationSampleCount}",
                sensorId,
                calibrationSampleCount);
            return false;
        }

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
                registrationContext.Position,
                async newNodeId =>
                {
                    sensor.IsActive = true;
                    sensor.NodeId = newNodeId;
                    sensor.ActivatedAt = DateTime.UtcNow;
                    sensor.EmptyDistanceMm = baselineDistanceMm;
                    sensor.CalibratedAtUtc = DateTime.UtcNow;
                    sensor.CalibrationSampleCount = calibrationSampleCount;
                    sensor.CalibrationMethod = "median";
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
            "Sensor {SensorId} activated as node {NodeId} from input (lat={InputLatitude}, lon={InputLongitude}) snapped to (lat={SnappedLatitude}, lon={SnappedLongitude})",
            sensorId,
            sensor.NodeId,
            registrationContext.Position.Latitude,
            registrationContext.Position.Longitude,
            await db.GraphNodes
                .AsNoTracking()
                .Where(node => node.Id == sensor.NodeId)
                .Select(node => (double?)node.Latitude)
                .SingleOrDefaultAsync(ct),
            await db.GraphNodes
                .AsNoTracking()
                .Where(node => node.Id == sensor.NodeId)
                .Select(node => (double?)node.Longitude)
                .SingleOrDefaultAsync(ct)
        );

        return true;
    }
}
