using Domain.ValueObjects;

namespace Application.Common.Dtos;

public sealed class MockRegistrationRequestDto
{
    public required Position Position { get; init; }
    public required int BaselineDistanceMm { get; init; }
    public required int DesiredReadingMm { get; init; }
}