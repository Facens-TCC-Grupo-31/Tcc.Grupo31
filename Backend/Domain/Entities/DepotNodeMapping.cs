namespace Domain.Entities;

public sealed class DepotNodeMapping
{
    public long Id { get; set; }

    public string DepotKey { get; set; } = string.Empty;
    public string GraphSignature { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public int NodeId { get; set; }
    public GraphNode Node { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
