namespace Domain.Entities;

public sealed class SensorDistanceMeasurement
{
    public long Id { get; set; }

    public long SensorId { get; set; }
    public Sensor Sensor { get; set; } = null!;

    public int DistanceMm { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string Source { get; set; } = null!;
}
