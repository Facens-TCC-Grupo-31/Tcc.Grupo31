namespace Infrastructure.Cache.Options;

public class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConfigurationString { get; set; } = default!;
}