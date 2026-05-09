using System.Xml.Linq;
using Domain.Entities;
using Infrastructure.Database;
using Infrastructure.Database.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: GraphImporter <path-to-osm-file>");
    return 1;
}

string osmPath = args[0];

if (!File.Exists(osmPath))
{
    Console.Error.WriteLine($"File not found: {osmPath}");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.AddDatabase(builder.Configuration);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

Console.WriteLine("Loading OSM file...");
var doc = XDocument.Load(osmPath);
var ns = doc.Root!.Name.Namespace;

// Step 1 — Parse all nodes into a temporary dictionary keyed by OSM id
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

// Step 2 — Parse traversable ways (those with a highway tag)
var highwayWays = doc.Root
    .Elements(ns + "way")
    .Where(w => w.Elements(ns + "tag").Any(t => (string?)t.Attribute("k") == "highway"))
    .Select(w => w.Elements(ns + "nd")
        .Select(nd => (long)nd.Attribute("ref")!)
        .ToList())
    .Where(refs => refs.Count >= 2)
    .ToList();

Console.WriteLine($"Found {highwayWays.Count} traversable ways.");

// Step 3 — Collect only OSM node ids referenced by highway ways
var referencedOsmIds = highwayWays
    .SelectMany(refs => refs)
    .ToHashSet();

// Step 4 — Build GraphNode instances for referenced nodes only
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
await db.SaveChangesAsync();

// Step 5 — Generate deduplicated bidirectional edges
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
await db.SaveChangesAsync();

Console.WriteLine("Import complete.");
return 0;

static double CalculateDistance(GraphNode a, GraphNode b)
{
    double dx = b.X - a.X;
    double dy = b.Y - a.Y;
    return Math.Sqrt(dx * dx + dy * dy);
}
