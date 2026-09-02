using Application.Common.Utils;
using Application.Services;
using Infrastructure.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection routingSection = configuration.GetSection(RoutingOptions.SectionName);
        IConfigurationSection sensorLivenessSection = configuration.GetSection(SensorLivenessOptions.SectionName);

        services
            .AddOptions<RoutingOptions>()
            .Configure(options => routingSection.Bind(options))
            .Validate(options => !string.IsNullOrWhiteSpace(options.DepotKey), "Routing:DepotKey is required")
            .Validate(options => options.DepotLatitude >= -90 && options.DepotLatitude <= 90, "Routing:DepotLatitude must be between -90 and 90")
            .Validate(options => options.DepotLongitude >= -180 && options.DepotLongitude <= 180, "Routing:DepotLongitude must be between -180 and 180")
            .Validate(options => options.FillThreshold >= 0f && options.FillThreshold <= 1f, "Routing:FillThreshold must be between 0 and 1")
            .ValidateOnStart();

        services
            .AddOptions<SensorLivenessOptions>()
            .Configure(options => sensorLivenessSection.Bind(options))
            .Validate(options => options.HeartbeatTtl > TimeSpan.Zero,
                "SensorLiveness:HeartbeatTtl must be greater than 00:00:00")
            .ValidateOnStart();

        services.AddSingleton<ICoordinateDistanceCalculator, LocalApproximateDistanceCalculator>()
            .AddScoped<IGraphService, GraphService>()
            .AddScoped<IReadingService, ReadingService>()
            .AddScoped<ISensorQueryService, SensorQueryService>()
            .AddScoped<ISensorLifecycleService, SensorLifecycleService>()
            .AddScoped<ISensorRegistrationService, SensorRegistrationService>()
            .AddScoped<IPointSelectionStrategy, ThresholdPointSelectionStrategy>()
            .AddScoped<IShortestPathStrategy, DijkstraShortestPathStrategy>()
            .AddScoped<IRoutePlanningStrategy, ThresholdNearestNeighborMetricTspPlanner>()
            .AddScoped<IDepotNodeService, DepotNodeService>()
            .AddScoped<IRouteOrderingStrategy, NearestNeighborMetricTspOrderingStrategy>()
            .AddScoped<ICollectionRoutingService, CollectionRoutingService>();

        return services;
    }
}
