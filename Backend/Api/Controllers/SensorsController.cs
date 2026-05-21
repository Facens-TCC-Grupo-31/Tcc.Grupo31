using Application.Common.Dtos;
using Application.Services;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/sensor-nodes")]
public sealed class SensorsController(
    ISensorRegistrationService registrationService,
    IReadingService readingService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<RegistrationResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegistrationRequestDto request,
        CancellationToken ct)
    {
        if (TryBuildInvalidPositionProblem(request.Position, out ProblemDetails? problem))
        {
            return BadRequest(problem);
        }

        var result = await registrationService.RequestRegistrationAsync(
            request.Position,
            ct
        );

        return CreatedAtAction(
            nameof(Register),
            result
        );
    }

    [HttpPost("register/mock")]
    [ProducesResponseType<RegistrationResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterMock(
        [FromBody] MockRegistrationRequestDto request,
        CancellationToken ct)
    {
        if (TryBuildInvalidPositionProblem(request.Position, out ProblemDetails? problem))
        {
            return BadRequest(problem);
        }

        if (request.BaselineDistanceMm <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid mock baseline",
                Detail = "baselineDistanceMm must be greater than 0.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.DesiredReadingMm <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid mock desired reading",
                Detail = "desiredReadingMm must be greater than 0.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        RegistrationResponseDto result;
        try
        {
            result = await registrationService.RequestMockRegistrationAsync(
                request.Position,
                request.BaselineDistanceMm,
                request.DesiredReadingMm,
                ct
            );
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid mock registration payload",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                title: "Unable to register mock sensor",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(
            nameof(RegisterMock),
            result
        );
    }

    [HttpGet("{sensorId:long}/latest")]
    [ProducesResponseType<ReadingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(long sensorId, CancellationToken ct)
    {
        ReadingDto? latest = await readingService.GetLatestAsync(sensorId, ct);
        if (latest is null)
        {
            return NotFound();
        }

        return Ok(latest);
    }

    [HttpGet("{sensorId:long}/readings")]
    [ProducesResponseType<IReadOnlyList<ReadingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReadings(
        long sensorId,
        [FromQuery] GetReadingsQueryParams queryParams,
        CancellationToken ct
    )
    {
        if (queryParams.From.HasValue && queryParams.To.HasValue && queryParams.From > queryParams.To)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid time range",
                Detail = "Query parameter 'from' must be less than or equal to 'to'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var readings = await readingService.GetReadingsAsync(
            sensorId,
            queryParams.From,
            queryParams.To,
            ct
        );

        var response = readings.Select(r => new ReadingDto
        {
            SensorId = r.SensorId,
            FillLevel = r.FillLevel
        }
        ).ToList();

        return Ok(response);
    }

    public record GetReadingsQueryParams(DateTime? From, DateTime? To);

    private static bool TryBuildInvalidPositionProblem(Position position, out ProblemDetails? problem)
    {
        if (position.Latitude < -90 || position.Latitude > 90)
        {
            problem = new ProblemDetails
            {
                Title = "Invalid latitude",
                Detail = "position.latitude must be between -90 and 90.",
                Status = StatusCodes.Status400BadRequest
            };
            return true;
        }

        if (position.Longitude < -180 || position.Longitude > 180)
        {
            problem = new ProblemDetails
            {
                Title = "Invalid longitude",
                Detail = "position.longitude must be between -180 and 180.",
                Status = StatusCodes.Status400BadRequest
            };
            return true;
        }

        problem = null;
        return false;
    }
}
