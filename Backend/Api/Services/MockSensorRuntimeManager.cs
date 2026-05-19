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
    private string? _resolvedExecutablePath;

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

        if (!TryResolveExecutablePath(out _))
        {
            logger.LogWarning(
                "Failed to resolve executable path for mock sensor {SensorId}",
                sensorId);
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

        if (!TryResolveExecutablePath(out string resolvedPath))
        {
            logger.LogError("Failed to resolve mock runtime executable path.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        logger.LogInformation("Using mock runtime executable at {ExecutablePath}", resolvedPath);

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

    private bool TryResolveExecutablePath(out string executablePath)
    {
        executablePath = string.Empty;

        if (!string.IsNullOrWhiteSpace(_resolvedExecutablePath) && File.Exists(_resolvedExecutablePath))
        {
            executablePath = _resolvedExecutablePath;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            string configuredFullPath = Path.GetFullPath(_options.ExecutablePath);
            logger.LogInformation("Checking configured path: {ConfiguredPath}", configuredFullPath);
            if (File.Exists(configuredFullPath))
            {
                _resolvedExecutablePath = configuredFullPath;
                executablePath = configuredFullPath;
                logger.LogInformation("Resolved executable to configured path: {ResolvedPath}", configuredFullPath);
                return true;
            }
        }

        logger.LogInformation("Current working directory: {CurrentDirectory}", Directory.GetCurrentDirectory());
        logger.LogInformation("AppContext base directory: {BaseDirectory}", AppContext.BaseDirectory);

        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Native", "Platform", "Pc", "build-vcpkg", "Release", "pc_telemetry_runner.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Native", "Platform", "Pc", "build", "Release", "pc_telemetry_runner.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Native", "Platform", "Pc", "build-vcpkg", "Release", "pc_telemetry_runner.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Native", "Platform", "Pc", "build", "Release", "pc_telemetry_runner.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Native", "Platform", "Pc", "build-vcpkg", "Release", "pc_telemetry_runner.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Native", "Platform", "Pc", "build", "Release", "pc_telemetry_runner.exe"))
        ];

        foreach (string candidate in candidates)
        {
            logger.LogInformation("Checking candidate path: {CandidatePath}", candidate);
            if (!File.Exists(candidate))
            {
                continue;
            }

            _resolvedExecutablePath = candidate;
            executablePath = candidate;
            logger.LogInformation("Resolved executable path to: {ResolvedPath}", candidate);
            return true;
        }

        logger.LogWarning("No executable found in any candidate paths");

        return false;
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

        if (string.IsNullOrWhiteSpace(_resolvedExecutablePath))
        {
            logger.LogWarning(
                "Cannot start mock process for sensor {SensorId}: executable path is unresolved",
                sensorId);
            return Task.CompletedTask;
        }

        string readingArg = Math.Max(1, desiredReadingMm).ToString(CultureInfo.InvariantCulture);
        string args = $"{sensorId} {readingArg} {_options.BrokerUri}";

        var startInfo = new ProcessStartInfo
        {
            FileName = _resolvedExecutablePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_resolvedExecutablePath) ?? Environment.CurrentDirectory
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

        try
        {
            process.Start();
            _processBySensorId[sensorId] = process;

            logger.LogInformation(
                "Started mock process for sensor {SensorId} with desired reading {DesiredReadingMm}",
                sensorId,
                readingArg);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to start mock process for sensor {SensorId}. Executable={ExecutablePath} Args={Args}",
                sensorId,
                _resolvedExecutablePath,
                args);
            process.Dispose();
        }

        return Task.CompletedTask;
    }
}
