using Application.Common.Constants;
using MediatR;
using MQTTnet;

namespace Consumer;

public sealed class Worker(
    IConfiguration configuration,
    IMediator mediator,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string broker = configuration["Mqtt:Broker"]
            ?? throw new InvalidOperationException("MQTT Broker uri was not provided.");

        int port = int.TryParse(configuration["Mqtt:Port"], out int p)
            ? p
            : throw new InvalidOperationException("MQTT Broker port is invalid or was not provided");

        var factory = new MqttClientFactory();
        using var mqttClient = factory.CreateMqttClient();

        mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .WithClientId($"consumer-{Guid.NewGuid()}")
            .WithCleanSession()
            .Build();

        var connectResult = await mqttClient.ConnectAsync(options, stoppingToken);

        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
        {
            logger.LogError(
                "Failed to connect to MQTT broker at {Broker}:{Port} — {Reason}",
                broker,
                port,
                connectResult.ResultCode
            );

            return;
        }

        logger.LogInformation(
            "Connected to MQTT broker at {Broker}:{Port}",
            broker,
            port
        );

        await mqttClient.SubscribeAsync(
            new MqttTopicFilterBuilder().WithTopic(MqttTopics.Register).Build(),
            stoppingToken
        );

        await mqttClient.SubscribeAsync(
            new MqttTopicFilterBuilder().WithTopic(MqttTopics.Samples).Build(),
            stoppingToken
        );

        logger.LogInformation(
            "Subscribed to topics {Register} and {Samples}",
            MqttTopics.Register,
            MqttTopics.Samples
        );

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        string topic = e.ApplicationMessage.Topic;

        string payload = e.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : e.ApplicationMessage.ConvertPayloadToString();

        logger.LogInformation(
            "Received message on topic {Topic}: {Payload}",
            topic,
            payload
        );

        try
        {
            switch (topic)
            {
                case MqttTopics.Register:
                    await mediator.Send(new HandleRegisterMessageReceivedCommand(payload), default);
                    break;

                case MqttTopics.Samples:
                    await mediator.Send(new HandleSensorSampleReceivedCommand(payload), default);
                    break;

                default:
                    logger.LogWarning("Received message on unexpected topic {Topic}", topic);
                    return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing message on topic {Topic}", topic);
        }
    }
}
