using Application.Common.Dtos;

namespace Application.Services;

public interface ICollectionRoutingService
{
    Task<CollectionRouteResponseDtoV1> GenerateRouteAsync(CancellationToken ct = default);
}
