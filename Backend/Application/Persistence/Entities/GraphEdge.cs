namespace Application.Persistence.Entities;

public sealed class GraphEdge
{
    public int Id { get; set; }

    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }

    public double Distance { get; set; }
}
