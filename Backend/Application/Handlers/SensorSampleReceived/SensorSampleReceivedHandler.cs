using Application.Common.Constants;
using Application.Common.Dtos;
using Application.Handlers.SensorSampleReceived.Models;
using Application.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Handlers.SensorSampleReceived;

internal class SensorSampleReceivedHandler(
    IReadingService readingService,
    ILogger<ISensorSampleReceivedHandler> logger
) : ISensorSampleReceivedHandler
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task Handle(HandleSensorSampleReceivedCommand request, CancellationToken cancellationToken)
    {
        ReadingDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReadingDto>(
                request.Payload,
                _jsonSerializerOptions
            );
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize {Topic} payload: {Payload}",
                MqttTopics.Samples,
                request.Payload
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

        bool ok = await readingService.RegisterReadingAsync(
            dto.SensorId,
            dto.FillLevel,
            cancellationToken
        );
        if (!ok)
        {
            logger.LogWarning(
                "Sample rejected for sensor {SensorId} (inactive or invalid range)",
                dto.SensorId
            );
        }
    }
}
