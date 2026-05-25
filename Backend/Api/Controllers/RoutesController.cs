using Application.Common.Dtos;
using Application.Common.Enums;
using Application.Common.Exceptions;
using Application.Services;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/routes")]
public sealed class RoutesController(ICollectionRoutingService collectionRoutingService) : ControllerBase
{
    [HttpGet("collection")]
    [ProducesResponseType<CollectionRouteResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<CollectionRouteGeoJsonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetCollectionRoute(
        [FromQuery] CollectionRouteOutputFormat format = CollectionRouteOutputFormat.Json,
        [FromQuery] double? depotLatitude = null,
        [FromQuery] double? depotLongitude = null,
        [FromQuery] double? startLatitude = null,
        [FromQuery] double? startLongitude = null,
        [FromQuery] double? endLatitude = null,
        [FromQuery] double? endLongitude = null,
        CancellationToken ct = default)
    {
        try
        {
            if (TryBuildOptionalPosition("depotPosition", depotLatitude, depotLongitude, out Position? depotPosition, out ProblemDetails? depotProblem))
            {
                return BadRequest(depotProblem);
            }

            if (TryBuildOptionalPosition("startPosition", startLatitude, startLongitude, out Position? startPosition, out ProblemDetails? startProblem))
            {
                return BadRequest(startProblem);
            }

            if (TryBuildOptionalPosition("endPosition", endLatitude, endLongitude, out Position? endPosition, out ProblemDetails? endProblem))
            {
                return BadRequest(endProblem);
            }

            CollectionRouteResponseDto route = await collectionRoutingService.GenerateRouteAsync(
                new CollectionRouteRequestOptionsDto
                {
                    DepotPosition = depotPosition,
                    StartPosition = startPosition,
                    EndPosition = endPosition
                },
                ct);

            if (format == CollectionRouteOutputFormat.GeoJson)
            {
                return Ok(CollectionRouteGeoJsonDto.From(route));
            }

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

    private static bool TryBuildOptionalPosition(
        string name,
        double? latitude,
        double? longitude,
        out Position? position,
        out ProblemDetails? problem)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            position = null;
            problem = null;
            return false;
        }

        if (!latitude.HasValue || !longitude.HasValue)
        {
            position = null;
            problem = new ProblemDetails
            {
                Title = "Invalid route position",
                Detail = $"{name} requires both latitude and longitude when provided.",
                Status = StatusCodes.Status400BadRequest
            };
            return true;
        }

        if (latitude.Value < -90 || latitude.Value > 90)
        {
            position = null;
            problem = new ProblemDetails
            {
                Title = "Invalid route latitude",
                Detail = $"{name}.latitude must be between -90 and 90.",
                Status = StatusCodes.Status400BadRequest
            };
            return true;
        }

        if (longitude.Value < -180 || longitude.Value > 180)
        {
            position = null;
            problem = new ProblemDetails
            {
                Title = "Invalid route longitude",
                Detail = $"{name}.longitude must be between -180 and 180.",
                Status = StatusCodes.Status400BadRequest
            };
            return true;
        }

        position = new Position(latitude.Value, longitude.Value);
        problem = null;
        return false;
    }
}
