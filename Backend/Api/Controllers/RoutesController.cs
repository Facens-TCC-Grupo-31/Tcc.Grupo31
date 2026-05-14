using Application.Common.Dtos;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/routes")]
public sealed class RoutesController(ICollectionRoutingService collectionRoutingService) : ControllerBase
{
    [HttpGet("collection")]
    [ProducesResponseType<CollectionRouteResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCollectionRoute(CancellationToken ct)
    {
        //CollectionRouteResponseDto route = await collectionRoutingService.GenerateRouteAsync(ct);
        return Ok(new CollectionRouteResponseDto() { DepotCoordinates = new Position(0, 0), OrderedNodeCoordinates = [], RouteGenerationMs = 0, TotalDistance = 0 });
    }
}
