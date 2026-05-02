using Infrastructure.Cache.DependencyInjection;
using Infrastructure.Database.DependencyInjection;
using Infrastructure.Mqtt.DependencyInjection;
using Infrastructure.Services.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        => services
            .AddServices()
            .AddDatabase(configuration)
            .AddRedis(configuration)
            .AddMqtt(configuration);
}
