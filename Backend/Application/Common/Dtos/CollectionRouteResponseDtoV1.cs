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

public sealed class CollectionRouteResponseDtoV1
{
    public required int DepotNodeId { get; init; }
    public required IReadOnlyList<int> NodeVisitOrder { get; init; }
    public required IReadOnlyList<long> SensorVisitOrder { get; init; }

    public required int SelectedBins { get; init; }
    public required int VisitedBins { get; init; }

    public required double TotalDistance { get; init; }
    public required double AverageShortestPathCost { get; init; }

    public required int GraphNodeCount { get; init; }
    public required int GraphEdgeCount { get; init; }

    public required double MatrixGenerationMs { get; init; }
    public required double RouteGenerationMs { get; init; }
}

public sealed class CollectionRouteResponseDto
{
    public required Position DepotCoordinates { get; init; }
    public required IReadOnlyList<Position> OrderedNodeCoordinates { get; init; }
    public required IReadOnlyList<CollectionRouteSelectedSensorDto> SelectedSensors { get; init; }

    public required double TotalDistance { get; init; }
    public required double RouteGenerationMs { get; init; }
}
