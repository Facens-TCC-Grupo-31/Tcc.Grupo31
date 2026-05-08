using Application.Common.Constants;
using Application.Common.Dtos;
using Application.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Handlers.RegisterMessageReceived.Models;

internal class RegisterMessageReceivedHandler(
    ISensorRegistrationService registrationService,
    ILogger<IRegisterMessageReceivedHandler> logger
) : IRegisterMessageReceivedHandler
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task Handle(HandleRegisterMessageReceivedCommand request, CancellationToken cancellationToken)
    {
        RegisterMessageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegisterMessageDto>(
                request.Payload,
                _jsonSerializerOptions
            );
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Failed to deserialize {Topic} payload: {Payload}",
                MqttTopics.Register,
                request.Payload
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

        bool ok = await registrationService.CompleteRegistrationAsync(dto.SensorId, dto.ProvisioningToken);
        if (!ok)
        {
            logger.LogWarning(
                "Registration completion rejected for sensor {SensorId}",
                dto.SensorId
            );
        }
    }
}