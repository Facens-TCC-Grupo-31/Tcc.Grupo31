using Application.Cache;
using Application.Common.Dtos;
using Application.Common.Exceptions;
using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
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

        var nodeA = new GraphNode { Longitude = -47.4300, Latitude = -23.4700 };
        var nodeB = new GraphNode { Longitude = -47.4310, Latitude = -23.4710 };

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
        Assert.NotEmpty(route.Stops);
        Assert.Single(route.SelectedSensors);

        CollectionRouteSelectedSensorDto selectedSensor = route.SelectedSensors[0];
        Assert.Equal(sensor1.Id, selectedSensor.SensorId);
        Assert.Equal(nodeA.Id, selectedSensor.NodeId);
        Assert.Equal(0.90f, selectedSensor.FillLevel);
        Assert.NotEqual(DateTime.MinValue, selectedSensor.FillTimestamp);
        Assert.Equal(new Position(nodeA.Latitude, nodeA.Longitude), selectedSensor.Position);

        Assert.Equal(route.DepotCoordinates, route.Stops[0].Position);
        Assert.Equal(route.DepotCoordinates, route.Stops[^1].Position);

        int depotStopCount = route.Stops.Count(stop => stop.Position == route.DepotCoordinates);
        Assert.True(depotStopCount >= 2);

        Assert.True(route.TotalDistance >= 0);
        Assert.True(route.RouteGenerationMs > 0);
    }

    [Fact]
    public async Task GenerateRouteAsync_ReturnsExpandedGraphPath_ThroughIntermediateNodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "default",
                ["Routing:DepotLatitude"] = "0",
                ["Routing:DepotLongitude"] = "0.5",
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

        var nodeA = new GraphNode { Latitude = 0, Longitude = 0 };
        var nodeB = new GraphNode { Latitude = 0, Longitude = 1 };
        var nodeC = new GraphNode { Latitude = 0, Longitude = 2 };

        db.GraphNodes.AddRange(nodeA, nodeB, nodeC);
        await db.SaveChangesAsync();

        db.GraphEdges.AddRange(
            new GraphEdge { FromNodeId = nodeA.Id, ToNodeId = nodeB.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeB.Id, ToNodeId = nodeA.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeB.Id, ToNodeId = nodeC.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeC.Id, ToNodeId = nodeB.Id, Distance = 1 });
        await db.SaveChangesAsync();

        var sensorAtA = new Sensor { IsActive = true, NodeId = nodeA.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        var sensorAtC = new Sensor { IsActive = true, NodeId = nodeC.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.AddRange(sensorAtA, sensorAtC);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(sensorAtA.Id, 0.90f, DateTime.UtcNow);
        await cache.SetAsync(sensorAtC.Id, 0.95f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();

        var route = await routingService.GenerateRouteAsync();

        Assert.Contains(new Position(nodeB.Latitude, nodeB.Longitude), route.OrderedNodeCoordinates);

        int nodeBCount = route.OrderedNodeCoordinates.Count(position => position == new Position(nodeB.Latitude, nodeB.Longitude));
        Assert.True(nodeBCount >= 2);

        bool hasConsecutiveDuplicateCoordinates = route.OrderedNodeCoordinates
            .Zip(route.OrderedNodeCoordinates.Skip(1), (a, b) => a == b)
            .Any(x => x);
        Assert.False(hasConsecutiveDuplicateCoordinates);
    }

    [Fact]
    public async Task GenerateRouteAsync_UsesCustomStartAndEnd_WithDepotFallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "default",
                ["Routing:DepotLatitude"] = "0",
                ["Routing:DepotLongitude"] = "1",
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

        var nodeA = new GraphNode { Latitude = 0, Longitude = 0 };
        var nodeB = new GraphNode { Latitude = 0, Longitude = 1 };
        var nodeC = new GraphNode { Latitude = 0, Longitude = 2 };

        db.GraphNodes.AddRange(nodeA, nodeB, nodeC);
        await db.SaveChangesAsync();

        db.GraphEdges.AddRange(
            new GraphEdge { FromNodeId = nodeA.Id, ToNodeId = nodeB.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeB.Id, ToNodeId = nodeA.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeB.Id, ToNodeId = nodeC.Id, Distance = 1 },
            new GraphEdge { FromNodeId = nodeC.Id, ToNodeId = nodeB.Id, Distance = 1 });
        await db.SaveChangesAsync();

        var sensor = new Sensor { IsActive = true, NodeId = nodeB.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.Add(sensor);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(sensor.Id, 0.95f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();

        // Ensure configured depot initialization does not affect persistence-boundary assertions below.
        await routingService.GenerateRouteAsync();

        int nodeCountBefore = await db.GraphNodes.CountAsync();
        int edgeCountBefore = await db.GraphEdges.CountAsync();

        var route = await routingService.GenerateRouteAsync(new CollectionRouteRequestOptionsDto
        {
            StartPosition = new Position(0, 0),
            EndPosition = new Position(0, 2)
        });

        int nodeCountAfter = await db.GraphNodes.CountAsync();
        int edgeCountAfter = await db.GraphEdges.CountAsync();

        Assert.Equal(new Position(0, 1), route.DepotCoordinates);
        Assert.Equal(new Position(0, 0), route.Stops[0].Position);
        Assert.Equal(new Position(0, 2), route.Stops[^1].Position);
        Assert.Equal(nodeCountBefore, nodeCountAfter);
        Assert.Equal(edgeCountBefore, edgeCountAfter);
    }

    [Fact]
    public async Task GenerateRouteAsync_ReturnsSnappedDepotCoordinates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "default",
                ["Routing:DepotLatitude"] = "0.5",
                ["Routing:DepotLongitude"] = "0.2",
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

        var edgeStart = new GraphNode { Latitude = 0, Longitude = 0 };
        var edgeEnd = new GraphNode { Latitude = 1, Longitude = 0 };

        db.GraphNodes.AddRange(edgeStart, edgeEnd);
        await db.SaveChangesAsync();

        db.GraphEdges.AddRange(
            new GraphEdge { FromNodeId = edgeStart.Id, ToNodeId = edgeEnd.Id, Distance = 1 },
            new GraphEdge { FromNodeId = edgeEnd.Id, ToNodeId = edgeStart.Id, Distance = 1 });
        await db.SaveChangesAsync();

        var sensor = new Sensor { IsActive = true, NodeId = edgeEnd.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.Add(sensor);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(sensor.Id, 0.95f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();
        var route = await routingService.GenerateRouteAsync();

        var depotMapping = await db.DepotNodeMappings.SingleAsync();
        var snappedDepotNode = await db.GraphNodes.SingleAsync(node => node.Id == depotMapping.NodeId);

        Assert.Equal(new Position(snappedDepotNode.Latitude, snappedDepotNode.Longitude), route.DepotCoordinates);
        Assert.NotEqual(new Position(depotMapping.Latitude, depotMapping.Longitude), route.DepotCoordinates);
    }

    [Fact]
    public async Task GenerateRouteAsync_ReusesExistingDepotMapping_AcrossConsecutiveCalls()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "default",
                ["Routing:DepotLatitude"] = "0.5",
                ["Routing:DepotLongitude"] = "0.2",
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

        var edgeStart = new GraphNode { Latitude = 0, Longitude = 0 };
        var edgeEnd = new GraphNode { Latitude = 1, Longitude = 0 };

        db.GraphNodes.AddRange(edgeStart, edgeEnd);
        await db.SaveChangesAsync();

        db.GraphEdges.AddRange(
            new GraphEdge { FromNodeId = edgeStart.Id, ToNodeId = edgeEnd.Id, Distance = 1 },
            new GraphEdge { FromNodeId = edgeEnd.Id, ToNodeId = edgeStart.Id, Distance = 1 });
        await db.SaveChangesAsync();

        var sensor = new Sensor { IsActive = true, NodeId = edgeEnd.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.Add(sensor);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(sensor.Id, 0.95f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();

        var firstRoute = await routingService.GenerateRouteAsync();
        var secondRoute = await routingService.GenerateRouteAsync();

        Assert.Equal(firstRoute.DepotCoordinates, secondRoute.DepotCoordinates);

        int mappingCount = await db.DepotNodeMappings.CountAsync();
        Assert.Equal(1, mappingCount);
    }

    [Fact]
    public async Task GenerateRouteAsync_ThrowsUnreachableSelectedBinsException_WhenSelectedBinIsDisconnected()
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

        var nodeA = new GraphNode { Longitude = -47.4300, Latitude = -23.4700 };
        var nodeB = new GraphNode { Longitude = -47.4310, Latitude = -23.4710 };
        var nodeC = new GraphNode { Longitude = -47.5000, Latitude = -23.5000 };

        db.GraphNodes.AddRange(nodeA, nodeB, nodeC);
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

        var reachableSensor = new Sensor { IsActive = true, NodeId = nodeA.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        var disconnectedSensor = new Sensor { IsActive = true, NodeId = nodeC.Id, CreatedAt = DateTime.UtcNow, ActivatedAt = DateTime.UtcNow };
        db.Sensors.AddRange(reachableSensor, disconnectedSensor);
        await db.SaveChangesAsync();

        var cache = scope.ServiceProvider.GetRequiredService<ISensorLatestValueCache>();
        await cache.SetAsync(reachableSensor.Id, 0.95f, DateTime.UtcNow);
        await cache.SetAsync(disconnectedSensor.Id, 0.99f, DateTime.UtcNow);

        var routingService = scope.ServiceProvider.GetRequiredService<ICollectionRoutingService>();

        await Assert.ThrowsAsync<UnreachableSelectedBinsException>(() => routingService.GenerateRouteAsync());
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

        public Task RemoveAsync(long sensorId, CancellationToken ct = default)
        {
            _values.Remove(sensorId);
            return Task.CompletedTask;
        }
    }
}
