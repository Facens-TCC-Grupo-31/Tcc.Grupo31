namespace Simulation;

public interface ISimulationApplicationEnvironmentFactory
{
    Task<ISimulationApplicationEnvironment> CreateAsync(
        SimulationScenarioDefinition definition,
        CancellationToken ct = default);
}
