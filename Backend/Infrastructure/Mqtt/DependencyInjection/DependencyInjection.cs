using Infrastructure.Mqtt.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Mqtt.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddMqtt(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection mqttSection = configuration.GetSection(MqttOptions.SectionName);

        services
            .AddOptions<MqttOptions>()
            .Configure(options => mqttSection.Bind(options))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Broker), "Mqtt:Broker is required")
            .Validate(options => options.Port > 0, "Mqtt:Port must be greater than zero")
            .ValidateOnStart();

        services.AddSingleton<IMqttConsumer, MqttConsumer>();

        return services;
    }
}
