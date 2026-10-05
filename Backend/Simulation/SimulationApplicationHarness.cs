namespace Simulation;

public interface ISimulationApplicationEnvironment : IAsyncDisposable
{
    ISimulationRouteProvider RouteProvider { get; }
    ISimulationReadingSink? ReadingSink { get; }
}

public interface ISimulationApplicationEnvironmentFactory
{
    Task<ISimulationApplicationEnvironment> CreateAsync(
        SimulationScenarioDefinition definition,
        CancellationToken ct = default);
}

public sealed class SimulationApplicationHarness(
    ISimulationApplicationEnvironmentFactory environmentFactory,
    SimulationOrchestrator orchestrator)
{
    public async Task<SimulationRunResult> RunAsync(
        SimulationScenarioDefinition definition,
        DateTime startTimeUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using ISimulationApplicationEnvironment environment =
            await environmentFactory.CreateAsync(definition, ct);

        return await orchestrator.RunAsync(
            definition,
            environment.RouteProvider,
            startTimeUtc,
            environment.ReadingSink,
            ct
        );
    }
}