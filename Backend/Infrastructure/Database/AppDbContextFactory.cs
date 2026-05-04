using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace Infrastructure.Database;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? ResolveConnectionStringFromSettings()
            ?? throw new InvalidOperationException(
                "Database connection string was not provided. Set ConnectionStrings__DefaultConnection or configure Api/appsettings.json.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new AppDbContext(options);
    }

    private static string? ResolveConnectionStringFromSettings()
    {
        string? environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        var candidateDirectories = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.Combine(Directory.GetCurrentDirectory(), "Api"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Api"))
        };

        foreach (string directory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? connection = ReadConnectionFromDirectory(directory, environment);
            if (!string.IsNullOrWhiteSpace(connection))
            {
                return connection;
            }
        }

        return null;
    }

    private static string? ReadConnectionFromDirectory(string directory, string? environment)
    {
        string baseSettings = Path.Combine(directory, "appsettings.json");
        string? environmentSettings =
            string.IsNullOrWhiteSpace(environment)
                ? null
                : Path.Combine(directory, $"appsettings.{environment}.json");

        string? connection = ReadConnectionFromFile(baseSettings);

        if (!string.IsNullOrWhiteSpace(environmentSettings))
        {
            connection = ReadConnectionFromFile(environmentSettings) ?? connection;
        }

        return connection;
    }

    private static string? ReadConnectionFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        using var json = JsonDocument.Parse(File.ReadAllText(filePath));

        if (!json.RootElement.TryGetProperty("ConnectionStrings", out JsonElement connectionStrings))
        {
            return null;
        }

        if (!connectionStrings.TryGetProperty("DefaultConnection", out JsonElement defaultConnection))
        {
            return null;
        }

        return defaultConnection.GetString();
    }
}
