using Domain.ValueObjects;

namespace Application.Common.Utils;

public interface ICoordinateDistanceCalculator
{
    double CalculateDistanceMeters(Position from, Position to);
    double CalculateSquaredDistanceMeters(Position from, Position to);
    double CalculateDistanceMeters(double latitudeA, double longitudeA, double latitudeB, double longitudeB);
    double CalculateSquaredDistanceMeters(double latitudeA, double longitudeA, double latitudeB, double longitudeB);
}
