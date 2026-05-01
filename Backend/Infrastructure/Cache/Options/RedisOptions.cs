namespace Infrastructure.Cache.Options;

public class RedisOptions
{
    public const string SectionName = "Redis";

    public string Configuration { get; set; } = default!;
}