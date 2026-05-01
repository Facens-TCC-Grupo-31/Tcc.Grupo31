using Infrastructure.Cache.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Infrastructure.Cache.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var redisOptions = configuration
            .GetSection(RedisOptions.SectionName)
            .Get<RedisOptions>()
            ?? throw new InvalidOperationException($"Configuration section '{RedisOptions.SectionName}' is required.");
        
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions.ConfigurationString));

        return services;
    }
}
