namespace Application.Infrastructure.Abstractions;

public interface IProvisioningTokenStore
{
    Task SetAsync(long sensorId, string token, CancellationToken ct = default);
    Task<string?> ConsumeAsync(long sensorId, CancellationToken ct = default);
}
