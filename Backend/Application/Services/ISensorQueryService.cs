using Application.Common.Dtos;

namespace Application.Services;

public interface ISensorQueryService
{
    Task<SensorNodePagedResponseDto> GetPagedAsync(
        int currentPageNumber,
        int pageSize,
        CancellationToken ct = default);

    Task<SensorNodeDetailDto?> GetByIdAsync(long sensorId, CancellationToken ct = default);
}
