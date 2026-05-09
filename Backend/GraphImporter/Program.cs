using Infrastructure.Database;
using Infrastructure.Database.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: GraphImporter <path-to-osm-file> [--clear]");
    Console.Error.WriteLine("  --clear  Optional. Clear graph tables before import (default: false)");
    return 1;
}

string osmPath = args[0];
bool clearDb = args.Length > 1 && args[1].Equals("--clear", StringComparison.OrdinalIgnoreCase);

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

if (clearDb)
{
    Console.WriteLine("Clearing existing graph data...");
    await db.Database.ExecuteSqlAsync($"TRUNCATE TABLE \"GraphEdges\" CASCADE");
    await db.Database.ExecuteSqlAsync($"TRUNCATE TABLE \"GraphNodes\" CASCADE");
    await db.SaveChangesAsync();
    Console.WriteLine("Graph tables cleared.");
}

Console.WriteLine("Loading OSM file...");
await OsmGraphImporter.ImportAsync(db, osmPath);

Console.WriteLine("Import complete.");
return 0;
