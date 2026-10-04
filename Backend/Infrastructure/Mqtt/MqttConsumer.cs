using Application.Common.Constants;
using Application.Messaging;
using Infrastructure.Mqtt.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using System.Threading.Channels;

namespace Infrastructure.Mqtt;

public sealed class MqttConsumer(
    IMessageDispatcher messageDispatcher,
    IOptions<MqttOptions> mqttOptions,
    ILogger<MqttConsumer> logger) : IMqttConsumer
{
    private readonly Channel<(string Topic, string Payload)> _channel = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(10_000)
    {
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        int maxConcurrency = Environment.ProcessorCount * 2;
        Task[] workers = Enumerable.Range(0, maxConcurrency)
            .Select(_ => ProcessQueueAsync(cancellationToken))
            .ToArray();

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
                    connectResult.ResultCode
                );

                return;
            }

            logger.LogInformation(
                "Connected to MQTT broker at {Broker}:{Port}",
                options.Broker,
                options.Port
            );

            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(MqttTopics.Register).Build(), cancellationToken);
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(MqttTopics.Samples).Build(), cancellationToken);
            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(MqttTopics.MockSamples).Build(), cancellationToken);

            logger.LogInformation(
                "Subscribed to topics {RegisterTopic}, {SamplesTopic}, and {MockSamplesTopic}",
                MqttTopics.Register,
                MqttTopics.Samples,
                MqttTopics.MockSamples
            );

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
        _channel.Writer.TryWrite((
            args.ApplicationMessage.Topic,
            args.ApplicationMessage.ConvertPayloadToString())
        );
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var (topic, payload) in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await messageDispatcher.DispatchAsync(topic, payload, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message on topic {Topic}", topic);
            }
        }
    }
}
