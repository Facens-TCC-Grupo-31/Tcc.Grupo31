using Domain.ValueObjects;

namespace Application.Common.Dtos;

public sealed class CollectionRouteSelectedSensorDto
{
    public required long SensorId { get; init; }
    public required int NodeId { get; init; }
    public required Position Position { get; init; }
    public required float FillLevel { get; init; }
    public required DateTime FillTimestamp { get; init; }
}

public sealed class CollectionRouteStopDto
{
    public required int StopIndex { get; init; }
    public required int NodeId { get; init; }
    public required Position Position { get; init; }
}

public sealed class CollectionRouteResponseDto
{
    public required Position DepotCoordinates { get; init; }
    public required IReadOnlyList<Position> OrderedNodeCoordinates { get; init; }
    public required IReadOnlyList<CollectionRouteStopDto> Stops { get; init; }
    public required IReadOnlyList<CollectionRouteSelectedSensorDto> OrderedSelectedSensors { get; init; }

    public required double TotalDistanceMeters { get; init; }
    public required double RouteGenerationMs { get; init; }
}