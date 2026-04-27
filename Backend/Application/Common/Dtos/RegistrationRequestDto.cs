namespace Application.Common.Dtos;

public sealed class RegistrationRequestDto
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}
