using Application.Common.Dtos;

namespace Application.Services;

public interface ISensorRegistrationService
{
    Task<RegistrationResponseDto> RequestRegistrationAsync(
        double latitude,
        double longitude,
        CancellationToken ct = default
    );

    Task<bool> CompleteRegistrationAsync(
        long sensorId,
        string token,
        CancellationToken ct = default
    );
}
