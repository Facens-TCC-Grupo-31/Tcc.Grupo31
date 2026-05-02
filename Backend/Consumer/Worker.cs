using Infrastructure.Mqtt;

namespace Consumer;

public sealed class Worker(IMqttConsumer mqttConsumer) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => mqttConsumer.RunAsync(stoppingToken);
}
