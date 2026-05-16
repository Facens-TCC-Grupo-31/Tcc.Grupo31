namespace Application.Common.Dtos;

public sealed class RegisterMessageDto
{
    public required long SensorId { get; init; }
    public required string ProvisioningToken { get; init; }
    public required int BaselineDistanceMm { get; init; }
    public required short CalibrationSampleCount { get; init; }
}
