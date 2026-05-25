using Domain.ValueObjects;

namespace Application.Common.Dtos;

public sealed class CollectionRouteRequestOptionsDto
{
    public Position? DepotPosition { get; init; }
    public Position? StartPosition { get; init; }
    public Position? EndPosition { get; init; }
}