using MQTTnet;

namespace Consumer;

public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string broker = configuration["MQTT_BROKER"] ?? "localhost";
        int port = int.TryParse(configuration["MQTT_PORT"], out var configuredPort) ? configuredPort : 1883;
        string clientId = Guid.NewGuid().ToString();
        string topic = "devices/samples";

        var factory = new MqttClientFactory();

        var mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .WithClientId(clientId)
            .WithCleanSession()
            .Build();

        var connectResult = await mqttClient.ConnectAsync(options, stoppingToken);

        if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
        {
            logger.LogInformation("Connected to MQTT broker at {Broker}:{Port}", broker, port);

            mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                string payload = e.ApplicationMessage.Payload.IsEmpty
                    ? string.Empty
                    : e.ApplicationMessage.ConvertPayloadToString();

                logger.LogInformation("Received message on topic {Topic}: {Payload}", e.ApplicationMessage.Topic, payload);

                return Task.CompletedTask;
            };

            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topic).Build(), stoppingToken);

            logger.LogInformation("Subscribed to topic {Topic}", topic);
        }
        else
        {
            logger.LogError("Failed to connect to MQTT broker: {Reason}", connectResult.ResultCode);
        }
    }
}
