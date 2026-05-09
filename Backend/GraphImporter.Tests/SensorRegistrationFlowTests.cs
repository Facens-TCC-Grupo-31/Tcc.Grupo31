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

public sealed class SensorRegistrationFlowTests
{
    [Fact]
    public async Task CompleteRegistrationAsync_InsertsNewNode_ForVeryCloseCoordinates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:DepotKey"] = "test",
                ["Routing:DepotLatitude"] = "-23.47",
                ["Routing:DepotLongitude"] = "-47.43",
                ["Routing:FillThreshold"] = "0.8"
            })
            .Build();

        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IProvisioningDataCache, InMemoryProvisioningDataCache>();
        services.AddServices(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var nodeA = new GraphNode { X = -47.4300000000, Y = -23.4700000000 };
        var nodeB = new GraphNode { X = -47.4300000002, Y = -23.4700000002 };

        db.GraphNodes.AddRange(nodeA, nodeB);
        await db.SaveChangesAsync();

        db.GraphEdges.Add(new GraphEdge
        {
            FromNodeId = nodeA.Id,
            ToNodeId = nodeB.Id,
            Distance = Math.Sqrt(Math.Pow(nodeB.X - nodeA.X, 2) + Math.Pow(nodeB.Y - nodeA.Y, 2))
        });
        await db.SaveChangesAsync();

        var sensor = new Sensor
        {
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Sensors.Add(sensor);
        await db.SaveChangesAsync();

        const string token = "token-close-nodes";
        double registrationLat = -23.47000000005;
        double registrationLon = -47.43000000005;

        var cache = scope.ServiceProvider.GetRequiredService<IProvisioningDataCache>();
        await cache.SetAsync(
            sensor.Id,
            new ProvisioningRegistrationContext(token, registrationLat, registrationLon));

        var registrationService = scope.ServiceProvider.GetRequiredService<ISensorRegistrationService>();

        bool ok = await registrationService.CompleteRegistrationAsync(sensor.Id, token);

        Assert.True(ok);

        var updatedSensor = await db.Sensors.SingleAsync(s => s.Id == sensor.Id);
        Assert.True(updatedSensor.IsActive);
        Assert.NotNull(updatedSensor.NodeId);

        var nodes = await db.GraphNodes.OrderBy(n => n.Id).ToListAsync();
        var edges = await db.GraphEdges.OrderBy(e => e.Id).ToListAsync();

        // Verify node insertion and edge splitting
        Assert.Equal(3, nodes.Count);
        Assert.Equal(2, edges.Count);

        GraphNode insertedNode = nodes.Single(n => n.Id == updatedSensor.NodeId);

        // Verify new node is distinct from endpoints
        Assert.NotEqual(nodeA.Id, insertedNode.Id);
        Assert.NotEqual(nodeB.Id, insertedNode.Id);

        // Verify inserted node is on the segment (within bounds)
        Assert.InRange(insertedNode.X, Math.Min(nodeA.X, nodeB.X), Math.Max(nodeA.X, nodeB.X));
        Assert.InRange(insertedNode.Y, Math.Min(nodeA.Y, nodeB.Y), Math.Max(nodeA.Y, nodeB.Y));

        // Ensure the new node did not collapse into either endpoint despite very close coordinates
        Assert.True(Math.Abs(insertedNode.X - nodeA.X) > 1e-14);
        Assert.True(Math.Abs(insertedNode.X - nodeB.X) > 1e-14);
        Assert.True(Math.Abs(insertedNode.Y - nodeA.Y) > 1e-14);
        Assert.True(Math.Abs(insertedNode.Y - nodeB.Y) > 1e-14);

        // Verify edge-splitting: both new edges reference the inserted node
        GraphEdge edge1 = edges[0];
        GraphEdge edge2 = edges[1];

        // At least one edge should have insertedNode as a node
        bool insertedNodeInEdges = (edge1.FromNodeId == insertedNode.Id || edge1.ToNodeId == insertedNode.Id) &&
                                   (edge2.FromNodeId == insertedNode.Id || edge2.ToNodeId == insertedNode.Id);
        Assert.True(insertedNodeInEdges, "Both new edges must reference the inserted node");

        // Verify that one edge connects from nodeA/nodeB to insertedNode, and the other from insertedNode to the other endpoint
        bool edge1FromA = edge1.FromNodeId == nodeA.Id || edge1.ToNodeId == nodeA.Id;
        bool edge1FromB = edge1.FromNodeId == nodeB.Id || edge1.ToNodeId == nodeB.Id;
        bool edge2FromA = edge2.FromNodeId == nodeA.Id || edge2.ToNodeId == nodeA.Id;
        bool edge2FromB = edge2.FromNodeId == nodeB.Id || edge2.ToNodeId == nodeB.Id;
        Assert.True((edge1FromA && edge2FromB) || (edge1FromB && edge2FromA),
            "New edges must connect the original endpoints to the inserted node");

        // Calculate distances for validation
        double origDist = Math.Sqrt(Math.Pow(nodeB.X - nodeA.X, 2) + Math.Pow(nodeB.Y - nodeA.Y, 2));
        double dist1 = Math.Sqrt(Math.Pow(edge1.Distance * edge1.Distance, 1));
        double dist2 = Math.Sqrt(Math.Pow(edge2.Distance * edge2.Distance, 1));
        double sumDist = edge1.Distance + edge2.Distance;

        // Verify distances sum to approximately the original distance (within floating-point tolerance)
        Assert.True(Math.Abs(sumDist - origDist) < 1e-10,
            $"Sum of new edge distances ({sumDist}) should equal original distance ({origDist})");

        // Verify both edges have positive distances
        Assert.True(edge1.Distance > 0, "New edge1 distance must be positive");
        Assert.True(edge2.Distance > 0, "New edge2 distance must be positive");
    }

    private sealed class InMemoryProvisioningDataCache : IProvisioningDataCache
    {
        private readonly Dictionary<long, ProvisioningRegistrationContext> _store = [];

        public Task SetAsync(long sensorId, ProvisioningRegistrationContext context, CancellationToken ct = default)
        {
            _store[sensorId] = context;
            return Task.CompletedTask;
        }

        public Task<ProvisioningRegistrationContext?> ConsumeAsync(long sensorId, CancellationToken ct = default)
        {
            if (!_store.Remove(sensorId, out ProvisioningRegistrationContext? value))
            {
                return Task.FromResult<ProvisioningRegistrationContext?>(null);
            }

            return Task.FromResult<ProvisioningRegistrationContext?>(value);
        }
    }
}
