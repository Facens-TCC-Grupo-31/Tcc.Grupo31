namespace Simulation;

public interface ISimulationApplicationEnvironment : IAsyncDisposable
{
    ISimulationRouteProvider RouteProvider { get; }
    ISimulationReadingSink? ReadingSink { get; }
}
