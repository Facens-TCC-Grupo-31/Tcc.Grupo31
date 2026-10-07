using Application.Common.Dtos;
using Domain.ValueObjects;
using Xunit;

public sealed class CollectionRouteGeoJsonTests
{
    [Fact]
    public void From_ReturnsExpectedLayersAndMetadata()
    {
        var route = new CollectionRouteResponseDto
        {
            DepotCoordinates = new Position(-23.4699, -47.4299),
            OrderedNodeCoordinates =
            [
                new Position(-23.4699, -47.4299),
                new Position(-23.4693, -47.4305)
            ],
            Stops =
            [
                new CollectionRouteStopDto
                {
                    StopIndex = 0,
                    NodeId = 1,
                    Position = new Position(-23.4699, -47.4299)
                },
                new CollectionRouteStopDto
                {
                    StopIndex = 1,
                    NodeId = 5,
                    Position = new Position(-23.4693, -47.4305)
                },
                new CollectionRouteStopDto
                {
                    StopIndex = 2,
                    NodeId = 1,
                    Position = new Position(-23.4699, -47.4299)
                }
            ],
            OrderedSelectedSensors =
            [
                new CollectionRouteSelectedSensorDto
                {
                    SensorId = 101,
                    NodeId = 5,
                    Position = new Position(-23.4693, -47.4305),
                    FillLevel = 0.91f,
                    FillTimestamp = new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc)
                }
            ],
            TotalDistanceMeters = 1.25,
            RouteGenerationMs = 30.5
        };

        CollectionRouteGeoJsonDto geoJson = CollectionRouteGeoJsonDto.From(route);

        Assert.Equal("FeatureCollection", geoJson.Type);
        Assert.Equal(6, geoJson.Features.Count);

        Assert.Equal("route", geoJson.Features[0].Properties["layer"]);
        Assert.Equal("depot", geoJson.Features[1].Properties["layer"]);
        Assert.Equal("sensor", geoJson.Features[2].Properties["layer"]);
        Assert.Equal("stop", geoJson.Features[3].Properties["layer"]);
        Assert.Equal("stop", geoJson.Features[4].Properties["layer"]);
        Assert.Equal("stop", geoJson.Features[5].Properties["layer"]);

        Assert.Equal(0, geoJson.Features[3].Properties["stopIndex"]);
        Assert.Equal(1, geoJson.Features[4].Properties["stopIndex"]);
        Assert.Equal(2, geoJson.Features[5].Properties["stopIndex"]);

        Assert.Equal(101L, geoJson.Features[2].Properties["sensorId"]);
        Assert.Equal(5, geoJson.Features[2].Properties["nodeId"]);
        Assert.Equal(0.91f, geoJson.Features[2].Properties["fillLevel"]);
        Assert.Equal(new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc), geoJson.Features[2].Properties["fillTimestamp"]);
    }

    [Fact]
    public void From_UsesLongitudeLatitudeOrder()
    {
        var route = new CollectionRouteResponseDto
        {
            DepotCoordinates = new Position(10, 20),
            OrderedNodeCoordinates =
            [
                new Position(10, 20),
                new Position(11, 21)
            ],
            Stops =
            [
                new CollectionRouteStopDto
                {
                    StopIndex = 0,
                    NodeId = 99,
                    Position = new Position(10, 20)
                },
                new CollectionRouteStopDto
                {
                    StopIndex = 1,
                    NodeId = 100,
                    Position = new Position(11, 21)
                },
                new CollectionRouteStopDto
                {
                    StopIndex = 2,
                    NodeId = 99,
                    Position = new Position(10, 20)
                }
            ],
            OrderedSelectedSensors =
            [
                new CollectionRouteSelectedSensorDto
                {
                    SensorId = 1,
                    NodeId = 99,
                    Position = new Position(11, 21),
                    FillLevel = 0.8f,
                    FillTimestamp = DateTime.UtcNow
                }
            ],
            TotalDistanceMeters = 1,
            RouteGenerationMs = 1
        };

        CollectionRouteGeoJsonDto geoJson = CollectionRouteGeoJsonDto.From(route);

        var lineCoordinates = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<double>>>(geoJson.Features[0].Geometry.Coordinates);
        Assert.Equal(20, lineCoordinates[0][0]);
        Assert.Equal(10, lineCoordinates[0][1]);

        var depotCoordinates = Assert.IsAssignableFrom<IReadOnlyList<double>>(geoJson.Features[1].Geometry.Coordinates);
        Assert.Equal(20, depotCoordinates[0]);
        Assert.Equal(10, depotCoordinates[1]);

        var firstStopCoordinates = Assert.IsAssignableFrom<IReadOnlyList<double>>(geoJson.Features[3].Geometry.Coordinates);
        Assert.Equal(20, firstStopCoordinates[0]);
        Assert.Equal(10, firstStopCoordinates[1]);
    }
}