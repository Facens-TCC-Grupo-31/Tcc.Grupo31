using System.Text.Json;
using Application.Common.Dtos;
using Application.Mqtt;
using Application.Services.Abstractions;
using MQTTnet;

namespace Consumer;

public sealed class Worker(
    ISensorRegistrationService registrationService,
    IReadingService readingService,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
                    await HandleRegistrationAsync(payload);
                    break;

                case MqttTopics.Samples:
                    await HandleSampleAsync(payload);
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

    private async Task HandleRegistrationAsync(string payload)
    {
        RegisterMessageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegisterMessageDto>(
                payload,
                _jsonSerializerOptions
            );
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize {Topic} payload: {Payload}",
                MqttTopics.Register,
                payload
            );

            return;
        }

        if (dto is null)
        {
            logger.LogError(
                "Null deserialization result for {Topic} payload",
                MqttTopics.Register
            );

            return;
        }

        bool ok = await registrationService.CompleteRegistrationAsync(dto.SensorId, dto.Token);
        if (!ok)
        {
            logger.LogWarning(
                "Registration completion rejected for sensor {SensorId}",
                dto.SensorId
            );
        }
    }

    private async Task HandleSampleAsync(string payload)
    {
        ReadingDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReadingDto>(
                payload,
                _jsonSerializerOptions
            );
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize {Topic} payload: {Payload}",
                MqttTopics.Samples,
                payload
            );

            return;
        }

        if (dto is null)
        {
            logger.LogError(
                "Null deserialization result for {Topic} payload",
                MqttTopics.Samples
            );

            return;
        }

        bool ok = await readingService.RegisterReadingAsync(dto.SensorId, dto.FillLevel);
        if (!ok)
        {
            logger.LogWarning(
                "Sample rejected for sensor {SensorId} (inactive or invalid range)",
                dto.SensorId
            );
        }
    }
}
