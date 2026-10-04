namespace Simulation;

public static class SimulationOutput
{
    public static string FormatSummary(SimulationRunResult result, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        string label = string.IsNullOrWhiteSpace(prefix) ? string.Empty : $"{prefix} ";

        return string.Join(Environment.NewLine,
        [
            $"{label}Simulated duration: {result.SimulationDuration}",
            $"{label}Collections triggered: {result.CollectionCount}",
            $"{label}Overflow events: {result.OverflowEventCount}",
            $"{label}Total overflow duration: {result.TotalOverflowDuration}",
            $"{label}Maximum single-sensor overflow duration: {result.MaximumSingleSensorOverflowDuration}",
            $"{label}Total collected volume: {result.TotalCollectedVolumeLiters:0.00} L",
            $"{label}Total route distance: {result.TotalRouteDistance:0.00} km"
        ]);
    }
}