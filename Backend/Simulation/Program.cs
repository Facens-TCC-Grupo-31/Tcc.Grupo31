using Simulation;
using Simulation.ScenarioDefinitions;
using System.Diagnostics;

SimulationCommandLineOptions commandLine = SimulationCommandLineOptions.Parse(args);

var scenarioDefinition = commandLine.Scenario switch
{
    SimulationScenarioKind.Baseline => ScenarioDefinitions.Baseline,
    SimulationScenarioKind.Dynamic => ScenarioDefinitions.Dynamic,
    _ => throw new UnreachableException()
};

Console.WriteLine(scenarioDefinition.Graph.OsmPath);

var environmentFactory = new SqliteSimulationEnvironmentFactory();
var orchestrator = new SimulationOrchestrator();
var startTimeUtc = DateTime.UtcNow;

SimulationRunResult simulationResult;
await using (var environment = await environmentFactory.CreateAsync(scenarioDefinition))
{
    simulationResult = await orchestrator.RunAsync(
        scenarioDefinition,
        environment.RouteProvider,
        startTimeUtc,
        environment.ReadingSink
    );
}

Console.WriteLine(SimulationOutput.FormatSummary(simulationResult));

await new SimulationReportWriter()
    .WriteAsync(simulationResult, commandLine.Scenario.ToString())
    .ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                Console.WriteLine($"Report file written to: {task.Result}");
            else
                Console.WriteLine($"Failed to write report file: {task.Exception?.GetBaseException().Message}");
        }
    );
