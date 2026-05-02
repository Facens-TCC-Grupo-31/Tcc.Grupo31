using Application.Common.Constants;
using Application.Messaging;
using Infrastructure.Mqtt.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace Infrastructure.Mqtt;

public sealed class MqttConsumer(
    IMessageDispatcher messageDispatcher,
    IOptions<MqttOptions> mqttOptions,
    ILogger<MqttConsumer> logger) : IMqttConsumer
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        MqttOptions options = mqttOptions.Value;

        if (string.IsNullOrWhiteSpace(options.Broker))
        {
            throw new InvalidOperationException("MQTT broker is required.");
        }

        var factory = new MqttClientFactory();
        using IMqttClient mqttClient = factory.CreateMqttClient();

        mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        try
        {
            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(options.Broker, options.Port)
                .WithClientId($"consumer-{Guid.NewGuid()}")
                .WithCleanSession()
                .Build();

            var connectResult = await mqttClient.ConnectAsync(clientOptions, cancellationToken);

            if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
            {
                logger.LogError(
                    "Failed to connect to MQTT broker at {Broker}:{Port} ({ResultCode})",
                    options.Broker,
                    options.Port,
                    connectResult.ResultCode);
                return;
            }

            logger.LogInformation(
                "Connected to MQTT broker at {Broker}:{Port}",
                options.Broker,
                options.Port);

            await mqttClient.SubscribeAsync(
                new MqttTopicFilterBuilder().WithTopic(MqttTopics.Register).Build(),
                cancellationToken);

            await mqttClient.SubscribeAsync(
                new MqttTopicFilterBuilder().WithTopic(MqttTopics.Samples).Build(),
                cancellationToken);

            logger.LogInformation(
                "Subscribed to topics {RegisterTopic} and {SamplesTopic}",
                MqttTopics.Register,
                MqttTopics.Samples);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("MQTT consumer is stopping due to cancellation.");
        }
        finally
        {
            mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;

            if (mqttClient.IsConnected)
            {
                await mqttClient.DisconnectAsync();
                logger.LogInformation("Disconnected from MQTT broker.");
            }
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        string topic = args.ApplicationMessage.Topic;
        string payload = args.ApplicationMessage.Payload.IsEmpty
            ? string.Empty
            : args.ApplicationMessage.ConvertPayloadToString();

        logger.LogInformation("Received message on topic {Topic}", topic);

        try
        {
            await messageDispatcher.DispatchAsync(topic, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing message on topic {Topic}", topic);
        }
    }
}
