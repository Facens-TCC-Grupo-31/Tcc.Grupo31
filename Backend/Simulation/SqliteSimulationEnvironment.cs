using Application.Cache;
using Application.Common.Dtos;
using Application.Services;
using GraphImporter;
using Infrastructure.Database;
using Infrastructure.Mqtt.Configuration;
using Infrastructure.Services.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Simulation;

public sealed class SqliteSimulationEnvironmentFactory : ISimulationApplicationEnvironmentFactory
{
    public async Task<ISimulationApplicationEnvironment> CreateAsync(
        SimulationScenarioDefinition definition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!File.Exists(definition.Graph.OsmPath))
        {
            throw new FileNotFoundException("Simulation OSM file was not found.", definition.Graph.OsmPath);
        }

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "simulation",
                ["Routing:DepotLatitude"] = (definition.Sensors.FirstOrDefault()?.Position?.Latitude ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Routing:DepotLongitude"] = (definition.Sensors.FirstOrDefault()?.Position?.Longitude ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SensorLiveness:HeartbeatTtl"] = "00:10:00",
                ["Mqtt:Broker"] = "simulation",
                ["Mqtt:Port"] = "1883"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IProvisioningDataCache, InMemoryProvisioningDataCache>();
        services.AddSingleton<ISensorLatestValueCache, InMemorySensorLatestValueCache>();
        services.AddSingleton<ISensorLivenessCache, InMemorySensorLivenessCache>();
        services.AddOptions<MqttOptions>().Bind(configuration.GetSection(MqttOptions.SectionName));
        services.AddServices(configuration);

        ServiceProvider provider = services.BuildServiceProvider();
        AsyncServiceScope scope = provider.CreateAsyncScope();
        try
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            await OsmGraphImporter.ImportAsync(db, definition.Graph.OsmPath, ct);

            var sensorIds = new Dictionary<long, long>();
            ISensorRegistrationService registration =
                scope.ServiceProvider.GetRequiredService<ISensorRegistrationService>();

            foreach (SimulationSensorDefinition sensor in definition.Sensors)
            {
                if (sensor.Position is null)
                {
                    throw new InvalidOperationException(
                        $"Sensor {sensor.SensorId} must define a position for application setup.");
                }

                RegistrationResponseDto response = await registration.RequestMockRegistrationAsync(
                    sensor.Position,
                    sensor.BaselineDistanceMm,
                    sensor.DesiredReadingMm,
                    ct);
                sensorIds[sensor.SensorId] = response.SensorId;
            }

            ISimulationRouteProvider routeProvider = definition.Route is not null
                ? SimulationRouteProviderFactory.Create(definition)
                : SimulationRouteProviderFactory.Create(
                    definition,
                    scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>(),
                    sensorIds);

            ISimulationReadingSink readingSink = new InMemoryReadingSink(
                scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>(),
                sensorIds);

            return new SqliteSimulationEnvironment(
                provider,
                connection,
                scope,
                routeProvider,
                readingSink);
        }
        catch
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class SqliteSimulationEnvironment(
        ServiceProvider provider,
        SqliteConnection connection,
        AsyncServiceScope scope,
        ISimulationRouteProvider routeProvider,
        ISimulationReadingSink? readingSink) : ISimulationApplicationEnvironment
    {
        public ISimulationRouteProvider RouteProvider => routeProvider;
        public ISimulationReadingSink? ReadingSink => readingSink;

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class InMemoryProvisioningDataCache : IProvisioningDataCache
    {
        private readonly Dictionary<long, ProvisioningRegistrationContext> values = [];

        public Task SetAsync(long sensorId, ProvisioningRegistrationContext context, CancellationToken ct = default)
        {
            values[sensorId] = context;
            return Task.CompletedTask;
        }

        public Task<ProvisioningRegistrationContext?> ConsumeAsync(long sensorId, CancellationToken ct = default)
        {
            values.Remove(sensorId, out ProvisioningRegistrationContext? context);
            return Task.FromResult(context);
        }
    }

    private sealed class InMemorySensorLatestValueCache : ISensorLatestValueCache
    {
        private readonly Dictionary<long, SensorLatestValue> values = [];

        public Task SetAsync(long sensorId, float fillLevel, DateTime timestamp, CancellationToken ct = default)
        {
            values[sensorId] = new SensorLatestValue(fillLevel, timestamp);
            return Task.CompletedTask;
        }

        public Task<SensorLatestValue?> GetAsync(long sensorId, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(sensorId, out SensorLatestValue? value) ? value : null);

        public Task<IReadOnlyDictionary<long, SensorLatestValue>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyDictionary<long, SensorLatestValue>)new Dictionary<long, SensorLatestValue>(values));

        public Task RemoveAsync(long sensorId, CancellationToken ct = default)
        {
            values.Remove(sensorId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySensorLivenessCache : ISensorLivenessCache
    {
        private readonly HashSet<long> alive = [];

        public Task RefreshAsync(long sensorId, CancellationToken ct = default)
        {
            alive.Add(sensorId);
            return Task.CompletedTask;
        }

        public Task<bool> IsAliveAsync(long sensorId, CancellationToken ct = default) =>
            Task.FromResult(alive.Contains(sensorId));

        public Task RemoveAsync(long sensorId, CancellationToken ct = default)
        {
            alive.Remove(sensorId);
            return Task.CompletedTask;
        }
    }
}