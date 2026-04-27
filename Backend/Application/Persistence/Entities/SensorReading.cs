namespace Application.Persistence.Entities;

public sealed class SensorReading
{
    public long Id { get; set; }

    public long SensorId { get; set; }
    public Sensor Sensor { get; set; } = null!;

    public float FillLevel { get; set; }

    public DateTime Timestamp { get; set; }
}
