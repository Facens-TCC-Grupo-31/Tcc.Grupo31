namespace Application.Common.Dtos;

public sealed class SensorSampleMessageDto
{
    public required long SensorId { get; init; }
    public required int DistanceMm { get; init; }
}
