using Domain.ValueObjects;

namespace Application.Common.Utils;

public interface ICoordinateDistanceCalculator
{
    double Calculate(Position from, Position to);
    double CalculateSquared(Position from, Position to);
    double Calculate(double latitudeA, double longitudeA, double latitudeB, double longitudeB);
    double CalculateSquared(double latitudeA, double longitudeA, double latitudeB, double longitudeB);
}
