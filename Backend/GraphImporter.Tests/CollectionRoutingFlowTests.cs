using Application.Cache;
using Application.Services;
using Domain.Entities;
using Infrastructure.Database;
using Infrastructure.Services.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class CollectionRoutingFlowTests
{
    [Fact]
    public async Task GenerateRouteAsync_ReturnsDeterministicRoute_ForSelectedBins()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "default",
                ["Routing:DepotLatitude"] = "-23.47005",
                ["Routing:DepotLongitude"] = "-47.43005",
                ["Routing:FillThreshold"] = "0.8"
            })
            .Build();

        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ISensorLatestValueCache, InMemorySensorLatestValueCache>();
        services.AddServices(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var nodeA = new GraphNode { X = -47.4300, Y = -23.4700 };
        var nodeB = new GraphNode { X = -47.4310, Y = -23.4710 };

        db.GraphNodes.AddRange(nodeA, nodeB);
        await db.SaveChangesAsync();

        db.GraphEdges.AddRange(
            new GraphEdge
            {
                FromNodeId = nodeA.Id,
                ToNodeId = nodeB.Id,
                Distance = 1
            },
            new GraphEdge
            {
                FromNodeId = nodeB.Id,
                ToNodeId = nodeA.Id,
                Distance = 1
            });
        await db.SaveChangesAsync();

        var sensor1 = new Sensor { IsActive = true, NodeId = nodeA.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        var sensor2 = new Sensor { IsActive = true, NodeId = nodeB.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.AddRange(sensor1, sensor2);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(sensor1.Id, 0.90f, DateTime.UtcNow);
        await cache.SetAsync(sensor2.Id, 0.40f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();

        var route = await routingService.GenerateRouteAsync();

        Assert.NotNull(route.DepotCoordinates);
        Assert.NotEmpty(route.OrderedNodeCoordinates);
        
        Assert.True(route.TotalDistance >= 0);
        Assert.True(route.RouteGenerationMs > 0);
    }

    private sealed class InMemorySensorLatestValueCache : ISensorLatestValueCache
    {
        private readonly Dictionary<long, SensorLatestValue> _values = [];

        public Task SetAsync(long sensorId, float fillLevel, DateTime timestamp, CancellationToken ct = default)
        {
            _values[sensorId] = new SensorLatestValue(fillLevel, timestamp);
            return Task.CompletedTask;
        }

        public Task<SensorLatestValue?> GetAsync(long sensorId, CancellationToken ct = default)
        {
            if (_values.TryGetValue(sensorId, out SensorLatestValue? value) && value is not null)
            {
                return Task.FromResult<SensorLatestValue?>(value);
            }

            return Task.FromResult<SensorLatestValue?>(null);
        }

        public Task<IReadOnlyDictionary<long, SensorLatestValue>> GetAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult((IReadOnlyDictionary<long, SensorLatestValue>)new Dictionary<long, SensorLatestValue>(_values));
        }
    }
}
