using System.Collections.Frozen;

namespace Simulation;

public interface ICollectionTriggerPolicy
{
    bool ShouldTrigger(CollectionTriggerContext context);
}

public sealed record CollectionTriggerContext(
    DateTime Timestamp,
    TimeSpan ElapsedTime,
    int ActiveSensorCount,
    float AverageFillLevel,
    IReadOnlyList<long> CriticalSensorIds);

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

public sealed class ScheduledWeekdaysCollectionTriggerPolicy(TimeOnly? timeOfDay = null, params DayOfWeek[] scheduledDaysOfWeek) : ICollectionTriggerPolicy
{
    private FrozenSet<DayOfWeek> SchedulesDaysOfWeek { get; } = FrozenSet.Create(scheduledDaysOfWeek);

    public bool ShouldTrigger(CollectionTriggerContext context)
    {
        if (timeOfDay is not null && TimeOnly.FromDateTime(context.Timestamp) != timeOfDay)
        {
            return false;
        }

        return SchedulesDaysOfWeek.Contains(context.Timestamp.DayOfWeek);
    }
}
