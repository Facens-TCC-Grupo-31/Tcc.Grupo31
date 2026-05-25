using Application.Common.Dtos;

namespace Application.Services;

public interface ICollectionRoutingService
{
    Task<CollectionRouteResponseDto> GenerateRouteAsync(
        CollectionRouteRequestOptionsDto? options = null,
        CancellationToken ct = default);
}
