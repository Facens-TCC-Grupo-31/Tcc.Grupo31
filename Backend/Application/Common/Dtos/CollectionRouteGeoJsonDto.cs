namespace Application.Common.Dtos;

public sealed class CollectionRouteGeoJsonDto
{
    public required string Type { get; init; }
    public required IReadOnlyList<CollectionRouteGeoJsonFeatureDto> Features { get; init; }

    public static CollectionRouteGeoJsonDto From(CollectionRouteResponseDto route)
    {
        var features = new List<CollectionRouteGeoJsonFeatureDto>
        {
            BuildRouteLineFeature(route),
            BuildDepotFeature(route.DepotCoordinates)
        };

        features.AddRange(route.OrderedSelectedSensors.Select(BuildSensorFeature));
        features.AddRange(BuildStopFeatures(route.Stops));

        return new CollectionRouteGeoJsonDto
        {
            Type = "FeatureCollection",
            Features = features
        };
    }

    private static CollectionRouteGeoJsonFeatureDto BuildRouteLineFeature(CollectionRouteResponseDto route)
    {
        var lineCoordinates = route.OrderedNodeCoordinates
            .Select(position => (IReadOnlyList<double>)[position.Longitude, position.Latitude])
            .ToList();

        return new CollectionRouteGeoJsonFeatureDto
        {
            Type = "Feature",
            Geometry = new CollectionRouteGeoJsonGeometryDto
            {
                Type = "LineString",
                Coordinates = lineCoordinates
            },
            Properties = new Dictionary<string, object?>
            {
                ["layer"] = "route",
                ["totalDistance"] = route.TotalDistanceMeters,
                ["routeGenerationMs"] = route.RouteGenerationMs
            }
        };
    }

    private static CollectionRouteGeoJsonFeatureDto BuildDepotFeature(Domain.ValueObjects.Position depotCoordinates)
    {
        return new CollectionRouteGeoJsonFeatureDto
        {
            Type = "Feature",
            Geometry = new CollectionRouteGeoJsonGeometryDto
            {
                Type = "Point",
                Coordinates = (IReadOnlyList<double>)[depotCoordinates.Longitude, depotCoordinates.Latitude]
            },
            Properties = new Dictionary<string, object?>
            {
                ["layer"] = "depot"
            }
        };
    }

    private static CollectionRouteGeoJsonFeatureDto BuildSensorFeature(CollectionRouteSelectedSensorDto sensor)
    {
        return new CollectionRouteGeoJsonFeatureDto
        {
            Type = "Feature",
            Geometry = new CollectionRouteGeoJsonGeometryDto
            {
                Type = "Point",
                Coordinates = (IReadOnlyList<double>)[sensor.Position.Longitude, sensor.Position.Latitude]
            },
            Properties = new Dictionary<string, object?>
            {
                ["layer"] = "sensor",
                ["sensorId"] = sensor.SensorId,
                ["nodeId"] = sensor.NodeId,
                ["fillLevel"] = sensor.FillLevel,
                ["fillTimestamp"] = sensor.FillTimestamp
            }
        };
    }

    private static IReadOnlyList<CollectionRouteGeoJsonFeatureDto> BuildStopFeatures(
        IReadOnlyList<CollectionRouteStopDto> stops)
    {
        return stops
            .Select(stop => new CollectionRouteGeoJsonFeatureDto
            {
                Type = "Feature",
                Geometry = new CollectionRouteGeoJsonGeometryDto
                {
                    Type = "Point",
                    Coordinates = (IReadOnlyList<double>)[stop.Position.Longitude, stop.Position.Latitude]
                },
                Properties = new Dictionary<string, object?>
                {
                    ["layer"] = "stop",
                    ["stopIndex"] = stop.StopIndex,
                    ["nodeId"] = stop.NodeId
                }
            })
            .ToList();
    }
}

public sealed class CollectionRouteGeoJsonFeatureDto
{
    public required string Type { get; init; }
    public required CollectionRouteGeoJsonGeometryDto Geometry { get; init; }
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }
}

public sealed class CollectionRouteGeoJsonGeometryDto
{
    public required string Type { get; init; }
    public required object Coordinates { get; init; }
}