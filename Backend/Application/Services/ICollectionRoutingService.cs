using Application.Common.Dtos;

namespace Application.Services;

public interface ICollectionRoutingService
{
    Task<CollectionRouteResponseDto> GenerateRouteAsync(CancellationToken ct = default);
}
