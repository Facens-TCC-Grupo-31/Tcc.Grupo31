namespace Domain.Entities;

public sealed class Sensor
{
    public long Id { get; set; }

    public bool IsActive { get; set; }

    public int? NodeId { get; set; }
    public GraphNode? Node { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }

    public ICollection<SensorReading> Readings { get; set; } = [];
}
