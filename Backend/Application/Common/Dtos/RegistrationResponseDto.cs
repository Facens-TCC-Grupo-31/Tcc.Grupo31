namespace Application.Common.Dtos;

public sealed class RegistrationResponseDto
{
    public required long SensorId { get; init; }
    public required string ProvisioningToken { get; init; }
    public required string MqttBrokerUri { get; init; }
    public required string Ssid { get; init; }
    public required string Password { get; init; }
}
