using Application.Common.Dtos;
using Domain.Entities;

namespace Application.Services;

public interface IReadingService
{
    Task<bool> RegisterReadingAsync(
        long sensorId,
        float fillLevel,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<SensorReading>> GetReadingsAsync(
        long sensorId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default
    );

    Task<ReadingDto?> GetLatestAsync(long sensorId, CancellationToken ct = default);
}
