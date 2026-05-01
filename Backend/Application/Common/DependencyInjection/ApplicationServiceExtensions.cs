using Application.Cache.DependencyInjection;
using Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Common.DependencyInjection;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        string redisConnectionString)
    {
        services.AddInfrastructure(redisConnectionString);

        services.AddScoped<ISensorRegistrationService, SensorRegistrationService>();
        services.AddScoped<IReadingService, ReadingService>();
        services.AddScoped<IGraphService, GraphService>();

        return services;
    }

    public static IServiceCollection AddApplicationDbContext(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));
        return services;
    }
}
