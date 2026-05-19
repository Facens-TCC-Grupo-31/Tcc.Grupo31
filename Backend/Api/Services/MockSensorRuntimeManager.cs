using Application.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Api.Services;

public sealed class MockSensorRuntimeManager(
    IServiceScopeFactory scopeFactory,
    IOptions<MockRuntimeOptions> options,
    ILogger<MockSensorRuntimeManager> logger) : BackgroundService, IMockSensorRuntimeNotifier
{
    private readonly ConcurrentDictionary<long, Process> _processBySensorId = new();
    private readonly MockRuntimeOptions _options = options.Value;

    public async Task StartMockSensorAsync(
        long sensorId,
        int baselineDistanceMm,
        int desiredReadingMm,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            logger.LogWarning("Mock runtime is enabled but ExecutablePath is empty.");
            return;
        }

        _ = baselineDistanceMm;
        await StartProcessAsync(sensorId, desiredReadingMm, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Mock runtime manager is disabled.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        await StartPersistedMocksAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in _processBySensorId)
        {
            try
            {
                if (!pair.Value.HasExited)
                {
                    pair.Value.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop mock process for sensor {SensorId}", pair.Key);
            }
            finally
            {
                pair.Value.Dispose();
            }
        }

        _processBySensorId.Clear();

        return base.StopAsync(cancellationToken);
    }

    private async Task StartPersistedMocksAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mockSensors = await db.Sensors
            .AsNoTracking()
            .Where(s => s.IsActive && s.CalibrationMethod == "mock" && s.EmptyDistanceMm.HasValue && s.CalibrationSampleCount.HasValue)
            .Select(s => new
            {
                s.Id,
                BaselineDistanceMm = s.EmptyDistanceMm!.Value,
                DesiredReadingMm = (int)s.CalibrationSampleCount!.Value
            })
            .ToListAsync(ct);

        foreach (var sensor in mockSensors)
        {
            await StartMockSensorAsync(sensor.Id, sensor.BaselineDistanceMm, sensor.DesiredReadingMm, ct);
        }
    }

    private Task StartProcessAsync(long sensorId, int desiredReadingMm, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        if (_processBySensorId.TryGetValue(sensorId, out Process? existing) && !existing.HasExited)
        {
            return Task.CompletedTask;
        }

        string readingArg = Math.Max(1, desiredReadingMm).ToString(CultureInfo.InvariantCulture);
        string args = $"{sensorId} {readingArg} {_options.BrokerUri}";

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? Environment.CurrentDirectory
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) =>
        {
            _processBySensorId.TryRemove(sensorId, out _);
            process.Dispose();
            logger.LogWarning("Mock process exited for sensor {SensorId}", sensorId);
        };

        process.Start();
        _processBySensorId[sensorId] = process;

        logger.LogInformation(
            "Started mock process for sensor {SensorId} with desired reading {DesiredReadingMm}",
            sensorId,
            readingArg);

        return Task.CompletedTask;
    }
}
