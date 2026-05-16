using Application.Common.Dtos;
using Domain.ValueObjects;

namespace Application.Services;

public interface ISensorRegistrationService
{
    Task<RegistrationResponseDto> RequestRegistrationAsync(
        Position position,
        CancellationToken ct = default
    );

    Task<bool> CompleteRegistrationAsync(
        long sensorId,
        string token,
        CancellationToken ct = default
    );
}
