namespace Infrastructure.Mqtt;

public interface IMqttConsumer
{
    Task RunAsync(CancellationToken cancellationToken);
}
