namespace Application.Common.Dtos;

public sealed class RegistrationResponseDto
{
    public required long SensorNodeId { get; init; }
    public required string ProvisioningToken { get; init; }
}
