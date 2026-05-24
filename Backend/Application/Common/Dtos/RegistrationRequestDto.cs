using Domain.ValueObjects;

namespace Application.Common.Dtos;

public sealed class RegistrationRequestDto
{
    public Position? Position { get; init; }
}
