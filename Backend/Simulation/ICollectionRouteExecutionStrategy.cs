using Application.Common.Dtos;
using Domain.ValueObjects;

namespace Simulation;

public interface ICollectionRouteExecutionStrategy
{
    CollectionRouteExecutionDecision SelectExecution(CollectionRouteRequestOptionsDto requestOptions);
}

public enum CollectionRouteExecutionMode
{
    ApplicationStrategy,
    FixedBaselineRoute
}

public sealed record CollectionRouteExecutionDecision(
    CollectionRouteExecutionMode Mode,
    IReadOnlyList<Position> RouteCoordinates);

public sealed class FixedBaselineCollectionRouteExecutionStrategy(
    Position depot,
    IReadOnlyList<Position> fixedRouteCoordinates) : ICollectionRouteExecutionStrategy
{
    public Position Depot { get; } = depot;

    public IReadOnlyList<Position> FixedRouteCoordinates { get; } = fixedRouteCoordinates;

    public CollectionRouteExecutionDecision SelectExecution(CollectionRouteRequestOptionsDto requestOptions)
    {
        return new CollectionRouteExecutionDecision(
            CollectionRouteExecutionMode.FixedBaselineRoute,
            FixedRouteCoordinates);
    }
}