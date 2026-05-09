using Domain.Entities;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class OsmGraphImporterTests
{
    [Fact]
    public async Task ImportAsync_InsertsDistinctCloseNodes_WithExpectedPrecision()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new AppDbContext(options);

        // Seed a pre-existing node very close to imported geometry to ensure new close nodes remain distinct.
        db.GraphNodes.Add(new GraphNode
        {
            X = -47.43000000005,
            Y = -23.47000000005
        });
        await db.SaveChangesAsync();

        string osmPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(osmPath, """
<?xml version="1.0" encoding="UTF-8"?>
<osm version="0.6" generator="test">
  <node id="1" lat="-23.4700000000" lon="-47.4300000000" />
  <node id="2" lat="-23.4700000001" lon="-47.4300000001" />
  <node id="3" lat="-23.4700000002" lon="-47.4300000002" />
  <node id="99" lat="-23.4800000000" lon="-47.4400000000" />
  <way id="10">
    <nd ref="1" />
    <nd ref="2" />
    <nd ref="3" />
    <tag k="highway" v="footway" />
  </way>
</osm>
""");

            await OsmGraphImporter.ImportAsync(db, osmPath);

            var nodes = await db.GraphNodes.ToListAsync();
            var edges = await db.GraphEdges.ToListAsync();

            Assert.Equal(4, nodes.Count);
            Assert.Equal(4, edges.Count);

            Assert.Contains(nodes, n => Math.Abs(n.X - (-47.4300000001)) < 1e-13 && Math.Abs(n.Y - (-23.4700000001)) < 1e-13);
            Assert.DoesNotContain(nodes, n => Math.Abs(n.X - (-47.4400000000)) < 1e-13 && Math.Abs(n.Y - (-23.4800000000)) < 1e-13);

            var importedCloseNodes = nodes
                .Where(n => n.X <= -47.43 && n.X >= -47.4300000002)
                .OrderBy(n => n.X)
                .ToList();

            Assert.True(importedCloseNodes.Count >= 3);
            Assert.NotEqual(importedCloseNodes[0].Id, importedCloseNodes[1].Id);

            Assert.All(edges, e => Assert.True(e.Distance > 0));
            Assert.Contains(edges, e => e.FromNodeId != e.ToNodeId);
        }
        finally
        {
            File.Delete(osmPath);
        }
    }
}
