using Domain.ValueObjects;

namespace Application.Cache;

public sealed record ProvisioningRegistrationContext(string Token, Position Position);

public interface IProvisioningDataCache
{
    Task SetAsync(long sensorId, ProvisioningRegistrationContext context, CancellationToken ct = default);
    Task<ProvisioningRegistrationContext?> ConsumeAsync(long sensorId, CancellationToken ct = default);
}
