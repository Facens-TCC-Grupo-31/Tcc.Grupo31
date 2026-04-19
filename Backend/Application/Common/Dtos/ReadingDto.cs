namespace Application.Common.Dtos;

public class ReadingDto
{
    public required string SensorId { get; set; }
    public required long Timestamp { get; set; }
    public required float FillLevel { get; set; }
}
