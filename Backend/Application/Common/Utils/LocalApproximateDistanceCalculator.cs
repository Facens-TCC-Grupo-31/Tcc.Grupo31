using Domain.ValueObjects;

namespace Application.Common.Utils;

public sealed class LocalApproximateDistanceCalculator : ICoordinateDistanceCalculator
{
    private const double MetersPerDegree = 111_120.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    public double CalculateDistanceMeters(Position from, Position to)
        => CalculateDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude);

    public double CalculateSquaredDistanceMeters(Position from, Position to)
        => CalculateSquaredDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude);

    public double CalculateDistanceMeters(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
        => Math.Sqrt(CalculateSquaredDistanceMeters(latitudeA, longitudeA, latitudeB, longitudeB));

    public double CalculateSquaredDistanceMeters(double latA, double lonA, double latB, double lonB)
    {
        double latitudeDeltaMeters = (latA - latB) * MetersPerDegree;

        double averageLatitude = (latA + latB) / 2.0;

        double longitudeDeltaMeters = (lonA - lonB) * Math.Cos(averageLatitude * DegreesToRadians) * MetersPerDegree;

        return Math.Pow(latitudeDeltaMeters, 2) + Math.Pow(longitudeDeltaMeters, 2);
    }
}
