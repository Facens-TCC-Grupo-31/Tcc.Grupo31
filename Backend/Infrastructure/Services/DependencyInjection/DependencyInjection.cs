using Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IGraphService, GraphService>()
            .AddScoped<IReadingService, ReadingService>()
            .AddScoped<ISensorRegistrationService, SensorRegistrationService>();

        return services;
    }
}
