namespace Application.Common.Dtos;

public sealed class RegistrationResponseDto
{
    public required long SensorId { get; init; }
    public required string ProvisioningToken { get; init; }
}
