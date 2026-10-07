using System.Xml.Linq;
using Application.Common.Utils;
using Domain.Entities;
using Infrastructure.Database;

namespace GraphImporter;

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

        // Update to capture one-way directional tags alongside the node references
        var highwayWays = doc.Root
            .Elements(ns + "way")
            .Where(w => w.Elements(ns + "tag").Any(t => (string?)t.Attribute("k") == "highway"))
            .Select(w => new
            {
                Refs = w.Elements(ns + "nd").Select(nd => (long)nd.Attribute("ref")!).ToList(),
                IsOneWay = w.Elements(ns + "tag").Any(t => (string?)t.Attribute("k") == "oneway" &&
                    ((string?)t.Attribute("v") == "yes" || (string?)t.Attribute("v") == "true" || (string?)t.Attribute("v") == "1")),
                IsReverse = w.Elements(ns + "tag").Any(t => (string?)t.Attribute("k") == "oneway" && (string?)t.Attribute("v") == "-1")
            })
            .Where(w => w.Refs.Count >= 2)
            .ToList();

        Console.WriteLine($"Found {highwayWays.Count} traversable ways.");

        var referencedOsmIds = highwayWays
            .SelectMany(w => w.Refs)
            .ToHashSet();

        var osmIdToGraphNode = osmNodes
            .Where(kv => referencedOsmIds.Contains(kv.Key))
            .ToDictionary(
                kv => kv.Key,
                kv => new GraphNode
                {
                    Longitude = kv.Value.Lon,
                    Latitude = kv.Value.Lat
                });

        Console.WriteLine($"Persisting {osmIdToGraphNode.Count} graph nodes...");
        db.GraphNodes.AddRange(osmIdToGraphNode.Values);
        await db.SaveChangesAsync(cancellationToken);

        // edgeSet now tracks specific (From, To) directed tuples instead of unordered pairs
        var edgeSet = new HashSet<(int, int)>();
        var edges = new List<GraphEdge>();

        foreach (var way in highwayWays)
        {
            var refs = way.Refs;
            for (int i = 0; i < refs.Count - 1; i++)
            {
                if (!osmIdToGraphNode.TryGetValue(refs[i], out var from) ||
                    !osmIdToGraphNode.TryGetValue(refs[i + 1], out var to))
                    continue;

                double distance = new LocalApproximateDistanceCalculator().CalculateDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude);

                // Add forward edge unless OSM explicitly maps it as a reverse one-way street (-1)
                if (!way.IsReverse && edgeSet.Add((from.Id, to.Id)))
                {
                    edges.Add(new GraphEdge { FromNodeId = from.Id, ToNodeId = to.Id, Distance = distance });
                }

                // Add backward edge unless OSM explicitly maps it as a forward one-way street (yes/true/1)
                if (!way.IsOneWay && edgeSet.Add((to.Id, from.Id)))
                {
                    edges.Add(new GraphEdge { FromNodeId = to.Id, ToNodeId = from.Id, Distance = distance });
                }
            }
        }

        Console.WriteLine($"Persisting {edges.Count} graph edges...");
        db.GraphEdges.AddRange(edges);
        await db.SaveChangesAsync(cancellationToken);
    }
}