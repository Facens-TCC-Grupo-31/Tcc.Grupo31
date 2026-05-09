namespace Application.Services;

public interface IDepotNodeService
{
    Task<int> GetOrCreateDepotNodeIdAsync(CancellationToken ct = default);
}
