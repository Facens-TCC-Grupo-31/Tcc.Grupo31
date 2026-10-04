using Simulation;
using Simulation.ScenarioDefinitions;
using System.Diagnostics;

SimulationCommandLineOptions commandLine = SimulationCommandLineOptions.Parse(args);

bool isUsingDynamicRoute = commandLine.Scenario is SimulationScenarioKind.Dynamic;

//var scenarioDefinition = 

string logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Simulation", "logs");
Directory.CreateDirectory(logsDirectory);

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
    DateTime.UtcNow,
    logsDirectory
);

Console.WriteLine(SimulationOutput.FormatSummary(simulationResult));
await WriteReportAsync(commandLine.Scenario.ToString(), simulationResult)
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

static async Task<string> WriteReportAsync(string scenarioName, SimulationRunResult result)
{
    string logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Simulation", "logs");
    Directory.CreateDirectory(logsDirectory);
    string fileName = $"simulation-{scenarioName.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.json";
    string filePath = Path.Combine(logsDirectory, fileName);
    await SimulationReportWriter.WriteAsync(filePath, result, scenarioName);
    return filePath;
}