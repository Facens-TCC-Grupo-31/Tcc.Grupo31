namespace Simulation;


public sealed class AnalyticalScenarioExecutor(
    ISimulationApplicationEnvironmentFactory environmentFactory,
    SimulationOrchestrator orchestrator)
{
    public AnalyticalScenarioExecutor()
        : this(new SqliteSimulationEnvironmentFactory(), new SimulationOrchestrator())
    {
    }

    public async Task<SimulationRunResult> ExecuteAsync(
        SimulationScenarioDefinition definition,
        string scenarioName,
        DateTime startTimeUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);

        Console.WriteLine(definition.Graph.OsmPath);

        var harness = new SimulationApplicationHarness(environmentFactory, orchestrator);
        return await harness.RunAsync(definition, startTimeUtc, ct);
    }
}
