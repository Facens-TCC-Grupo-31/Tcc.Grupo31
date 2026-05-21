using Domain.ValueObjects;

namespace Domain.Entities;

public sealed class GraphNode
{
    public int Id { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }

    public Position Position => new(Latitude, Longitude);

    public ICollection<Sensor> Sensors { get; set; } = [];
}
