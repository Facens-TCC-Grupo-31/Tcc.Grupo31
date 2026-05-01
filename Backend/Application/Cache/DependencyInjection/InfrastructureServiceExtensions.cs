using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Application.Cache.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string redisConnectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddSingleton<IProvisioningTokenStore, RedisProvisioningTokenStore>();
        services.AddSingleton<ISensorLatestValueCache, RedisSensorLatestValueCache>();

        return services;
    }
}
