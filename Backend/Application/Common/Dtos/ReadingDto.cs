namespace Application.Common.Dtos;

public sealed class ReadingDto
{
    public required long SensorId { get; init; }
    public required float FillLevel { get; init; }
}
