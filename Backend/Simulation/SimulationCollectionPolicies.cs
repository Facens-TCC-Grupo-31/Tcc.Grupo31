using Application.Common.Dtos;
using Domain.ValueObjects;

namespace Simulation;

public enum CollectionRouteExecutionMode
{
    ApplicationStrategy,
    FixedBaselineRoute
}

public sealed record CollectionTriggerContext(
    DateTime Timestamp,
    TimeSpan ElapsedTime,
    int ActiveSensorCount,
    float AverageFillLevel,
    IReadOnlyList<long> CriticalSensorIds);

public sealed record CollectionRouteExecutionDecision(
    CollectionRouteExecutionMode Mode,
    IReadOnlyList<Position> RouteCoordinates);

public interface ICollectionTriggerPolicy
{
    bool ShouldTrigger(CollectionTriggerContext context);
}

public interface ICollectionRouteExecutionStrategy
{
    CollectionRouteExecutionDecision SelectExecution(CollectionRouteRequestOptionsDto requestOptions);
}

public sealed class AverageFillThresholdCollectionTriggerPolicy(float threshold) : ICollectionTriggerPolicy
{
    public float Threshold { get; } = threshold;

    public bool ShouldTrigger(CollectionTriggerContext context)
    {
        return context.ActiveSensorCount > 0 && context.AverageFillLevel >= Threshold;
    }
}

public sealed class SpacedCollectionTriggerPolicy(TimeSpan interval, TimeSpan initialOffset) : ICollectionTriggerPolicy
{
    public TimeSpan Interval { get; } = interval;
    public TimeSpan InitialOffset { get; } = initialOffset;
    public bool ShouldTrigger(CollectionTriggerContext context)
    {
        var shouldTrigger = context.ElapsedTime == InitialOffset || ((context.ElapsedTime - InitialOffset).Ticks % Interval.Ticks == 0);
        
        return shouldTrigger;
    }
}

public sealed class CriticalThresholdCollectionTriggerPolicy(float criticalThreshold) : ICollectionTriggerPolicy
{
    public float CriticalThreshold { get; } = criticalThreshold;

    public bool ShouldTrigger(CollectionTriggerContext context)
    {
        return context.CriticalSensorIds.Count > 0 || context.AverageFillLevel >= CriticalThreshold;
    }
}

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
