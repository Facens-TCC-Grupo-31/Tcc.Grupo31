namespace Simulation;

public interface ISimulationReadingSink
{
    Task PublishAsync(
        IReadOnlyList<SimulationSensorReading> readings,
        CancellationToken ct = default);
}