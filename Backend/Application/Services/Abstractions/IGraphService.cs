namespace Application.Services.Abstractions;

public interface IGraphService
{
    IDisposable? TryAcquireWriteLock();

    Task<int> ApplyNearestEdgeSplitAsync(
        double latitude,
        double longitude,
        Func<int, Task> applyMutation,
        CancellationToken ct = default
    );
}
