using Domain.ValueObjects;

namespace Application.Common.Dtos;

public sealed class RegistrationRequestDto
{
    public required Position Position { get; init; }
}
