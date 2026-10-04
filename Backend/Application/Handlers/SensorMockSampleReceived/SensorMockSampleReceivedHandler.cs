using Application.Common.Constants;
using Application.Common.Dtos;
using Application.Handlers.SensorMockSampleReceived.Models;
using Application.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Handlers.SensorMockSampleReceived;

internal class SensorMockSampleReceivedHandler(
    IReadingService readingService,
    ILogger<ISensorMockSampleReceivedHandler> logger
) : ISensorMockSampleReceivedHandler
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task Handle(HandleSensorMockSampleReceivedCommand request, CancellationToken cancellationToken)
    {
        SensorSampleMessageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SensorSampleMessageDto>(
                request.Payload,
                _jsonSerializerOptions
            );
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize {Topic} payload: {Payload}",
                MqttTopics.MockSamples,
                request.Payload
            );

            return;
        }

        if (dto is null)
        {
            logger.LogError(
                "Null deserialization result for {Topic} payload",
                MqttTopics.MockSamples
            );

            return;
        }

        bool ok = await readingService.RegisterMockReadingAsync(dto, cancellationToken);

        if (!ok)
        {
            logger.LogWarning(
                "Mock sample rejected for sensor {SensorId}",
                dto.SensorId
            );
        }
    }
}
