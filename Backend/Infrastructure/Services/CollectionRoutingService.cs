using Application.Common.Dtos;
using Application.Services;
using Infrastructure.Services.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Infrastructure.Services;

internal sealed class CollectionRoutingService(
    IDepotNodeService depotNodeService,
    IRoutePlanningStrategy routePlanningStrategy,
    IGraphService graphService,
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

        GraphStats graphStats = await graphService.GetGraphStatsAsync(ct);

        totalSw.Stop();

        return new CollectionRouteResponseDto
        {
            DepotNodeId = depotNodeId,
            NodeVisitOrder = planningResult.NodeVisitOrder,
            SensorVisitOrder = planningResult.SensorVisitOrder,
            SelectedBins = planningResult.SelectedBins,
            VisitedBins = planningResult.VisitedBins,
            TotalDistance = planningResult.TotalDistance,
            AverageShortestPathCost = planningResult.AverageShortestPathCost,
            GraphNodeCount = graphStats.NodeCount,
            GraphEdgeCount = graphStats.EdgeCount,
            MatrixGenerationMs = planningResult.MatrixGenerationMs,
            RouteGenerationMs = totalSw.Elapsed.TotalMilliseconds
        };
    }
}
