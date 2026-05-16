using Application.Common.Dtos;
using Application.Services;
using Infrastructure.Database;
using Infrastructure.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Infrastructure.Services;

internal sealed class CollectionRoutingService(
    IDepotNodeService depotNodeService,
    IRoutePlanningStrategy routePlanningStrategy,
    AppDbContext db,
    IOptions<RoutingOptions> options) : ICollectionRoutingService
{
    private readonly RoutingOptions _options = options.Value;

    public async Task<CollectionRouteResponseDto> GenerateRouteAsync(CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();

        int depotNodeId = await depotNodeService.GetOrCreateDepotNodeIdAsync(ct);
        RoutePlanningResult planningResult = await routePlanningStrategy.PlanAsync(
            new RoutePlanningRequest(depotNodeId, _options.FillThreshold),
            ct);

        // Fetch depot node position
        var depotNode = await db.GraphNodes.FindAsync(new object[] { depotNodeId }, cancellationToken: ct);
        if (depotNode == null)
            throw new InvalidOperationException($"Depot node {depotNodeId} not found");

        // Fetch ordered nodes and map to positions
        var orderedNodeIds = planningResult.NodeVisitOrder;
        var orderedNodes = await db.GraphNodes
            .Where(n => orderedNodeIds.Contains(n.Id))
            .ToListAsync(ct);

        var nodePositionMap = orderedNodes.ToDictionary(n => n.Id, n => n.Position);
        var orderedNodeCoordinates = orderedNodeIds
            .Select(nodeId => nodePositionMap[nodeId])
            .ToList();

        totalSw.Stop();

        return new CollectionRouteResponseDto
        {
            DepotCoordinates = depotNode.Position,
            OrderedNodeCoordinates = orderedNodeCoordinates,
            TotalDistance = planningResult.TotalDistance,
            RouteGenerationMs = totalSw.Elapsed.TotalMilliseconds
        };
    }
}
