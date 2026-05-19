using Application.Common.Dtos;
using Application.Common.Exceptions;
using Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/routes")]
public sealed class RoutesController(ICollectionRoutingService collectionRoutingService) : ControllerBase
{
    [HttpGet("collection")]
    [ProducesResponseType<CollectionRouteResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetCollectionRoute(CancellationToken ct)
    {
        try
        {
            CollectionRouteResponseDto route = await collectionRoutingService.GenerateRouteAsync(ct);
            return Ok(route);
        }
        catch (UnreachableSelectedBinsException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unreachable collection bins",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }
}
