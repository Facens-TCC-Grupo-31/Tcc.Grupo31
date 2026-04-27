namespace Application.Common.Dtos;

public sealed class RegisterMessageDto
{
    public required long SensorId { get; init; }
    public required string Token { get; init; }
}
