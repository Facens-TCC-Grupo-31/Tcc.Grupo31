using System.Xml.Linq;
using Domain.Entities;
using Infrastructure.Database;

public static class OsmGraphImporter
{
    public static async Task ImportAsync(AppDbContext db, string osmPath, CancellationToken cancellationToken = default)
    {
        var doc = XDocument.Load(osmPath);
        var ns = doc.Root!.Name.Namespace;

        var osmNodes = doc.Root
            .Elements(ns + "node")
            .ToDictionary(
                el => (long)el.Attribute("id")!,
                el => new
                {
                    Lat = (double)el.Attribute("lat")!,
                    Lon = (double)el.Attribute("lon")!
                });

        Console.WriteLine($"Parsed {osmNodes.Count} OSM nodes.");

        var highwayWays = doc.Root
            .Elements(ns + "way")
            .Where(w => w.Elements(ns + "tag").Any(t => (string?)t.Attribute("k") == "highway"))
            .Select(w => w.Elements(ns + "nd")
                .Select(nd => (long)nd.Attribute("ref")!)
                .ToList())
            .Where(refs => refs.Count >= 2)
            .ToList();

        Console.WriteLine($"Found {highwayWays.Count} traversable ways.");

        var referencedOsmIds = highwayWays
            .SelectMany(refs => refs)
            .ToHashSet();

        var osmIdToGraphNode = osmNodes
            .Where(kv => referencedOsmIds.Contains(kv.Key))
            .ToDictionary(
                kv => kv.Key,
                kv => new GraphNode
                {
                    X = kv.Value.Lon,
                    Y = kv.Value.Lat
                });

        Console.WriteLine($"Persisting {osmIdToGraphNode.Count} graph nodes...");
        db.GraphNodes.AddRange(osmIdToGraphNode.Values);
        await db.SaveChangesAsync(cancellationToken);

        var edgeSet = new HashSet<(int, int)>();
        var edges = new List<GraphEdge>();

        foreach (var refs in highwayWays)
        {
            for (int i = 0; i < refs.Count - 1; i++)
            {
                if (!osmIdToGraphNode.TryGetValue(refs[i], out var from) ||
                    !osmIdToGraphNode.TryGetValue(refs[i + 1], out var to))
                    continue;

                var key = from.Id < to.Id
                    ? (from.Id, to.Id)
                    : (to.Id, from.Id);

                if (!edgeSet.Add(key))
                    continue;

                double distance = CalculateDistance(from, to);

                edges.Add(new GraphEdge { FromNodeId = from.Id, ToNodeId = to.Id, Distance = distance });
                edges.Add(new GraphEdge { FromNodeId = to.Id, ToNodeId = from.Id, Distance = distance });
            }
        }

        Console.WriteLine($"Persisting {edges.Count} graph edges ({edges.Count / 2} unique pairs)...");
        db.GraphEdges.AddRange(edges);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static double CalculateDistance(GraphNode a, GraphNode b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
