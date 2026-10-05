using Simulation;
using Simulation.ScenarioDefinitions;
using System.Diagnostics;

SimulationCommandLineOptions commandLine = SimulationCommandLineOptions.Parse(args);

var executor = new AnalyticalScenarioExecutor();

var scenarioDefinition = commandLine.Scenario switch
{
    SimulationScenarioKind.Baseline => ScenarioDefinitions.Baseline,
    SimulationScenarioKind.Dynamic => ScenarioDefinitions.Dynamic,
    _ => throw new UnreachableException()
};

var simulationResult = await executor.ExecuteAsync(
    scenarioDefinition,
    commandLine.Scenario.ToString(),
    DateTime.UtcNow
);

Console.WriteLine(SimulationOutput.FormatSummary(simulationResult));

var simulationReportWriter = new SimulationReportWriter();

await simulationReportWriter.WriteAsync(simulationResult, commandLine.Scenario.ToString())
    .ContinueWith(task =>
    {
        if (task.IsCompletedSuccessfully)
        {
            Console.WriteLine($"Report file written to: {task.Result}");
        }
        else
        {
            Console.WriteLine($"Failed to write report file: {task.Exception?.GetBaseException().Message}");
        }
    });
