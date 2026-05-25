namespace Application.Common.Dtos;

public sealed class SensorNodeHealthDto
{
    public required bool IsAlive { get; init; }
    public required string HealthState { get; init; }
    public float? LatestFillLevel { get; init; }
    public DateTime? LatestReadingTimestamp { get; init; }
}

public sealed class SensorNodeDetailDto
{
    public required long Id { get; init; }
    public required bool IsActive { get; init; }
    public int? NodeId { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? ActivatedAt { get; init; }

    public int? EmptyDistanceMm { get; init; }
    public DateTime? CalibratedAtUtc { get; init; }
    public short? CalibrationSampleCount { get; init; }
    public string? CalibrationMethod { get; init; }

    public double? NodeLatitude { get; init; }
    public double? NodeLongitude { get; init; }

    public required SensorNodeHealthDto Health { get; init; }
}

public sealed class SensorNodePagedResponseDto
{
    public required int CurrentPageNumber { get; init; }
    public required int PageSize { get; init; }
    public required int TotalPages { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<SensorNodeDetailDto> Items { get; init; }
}
