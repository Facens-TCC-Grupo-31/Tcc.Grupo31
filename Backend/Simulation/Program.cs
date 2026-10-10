using Simulation;
using Simulation.ScenarioDefinitions;
using System.Diagnostics;

SimulationCommandLineOptions commandLine = SimulationCommandLineOptions.Parse(args);

var scenarioDefinition = commandLine.Scenario switch
{
    SimulationScenarioKind.Scenario_1_Baseline => ScenarioDefinitions.Scenario1.Baseline,
    SimulationScenarioKind.Scenario_1_Dynamic => ScenarioDefinitions.Scenario1.Dynamic,
    SimulationScenarioKind.Scenario_2_Baseline => ScenarioDefinitions.Scenario2.Baseline,
    SimulationScenarioKind.Scenario_2_Dynamic => ScenarioDefinitions.Scenario2.Dynamic,
    SimulationScenarioKind.Scenario_3_Baseline => ScenarioDefinitions.Scenario3.Baseline,
    SimulationScenarioKind.Scenario_3_Dynamic => ScenarioDefinitions.Scenario3.Dynamic,
    SimulationScenarioKind.Scenario_4_Baseline => ScenarioDefinitions.Scenario4.Baseline,
    SimulationScenarioKind.Scenario_4_Dynamic => ScenarioDefinitions.Scenario4.Dynamic,
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
