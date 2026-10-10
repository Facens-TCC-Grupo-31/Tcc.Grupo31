namespace Simulation;

using System;
using System.Collections.Generic;

public enum SimulationScenarioKind
{
    Scenario_0,

    Scenario_1_Baseline,
    Scenario_1_Dynamic,

    Scenario_2_Baseline,
    Scenario_2_Dynamic,
    
    Scenario_3_Baseline,
    Scenario_3_Dynamic,
    
    Scenario_4_Baseline,
    Scenario_4_Dynamic,
}

public sealed record SimulationCommandLineOptions(SimulationScenarioKind Scenario, bool RunAll = false)
{
    private const string ScenarioOption = "--scenario";
    private const string AllOption = "--all";

    public static string Usage =>
        "Usage: Simulation [--all | --scenario scenario-0 | scenario-{1-4}-baseline | scenario-{1-4}-dynamic]";

    public IReadOnlyList<SimulationScenarioKind> Scenarios => RunAll
        ? Enum.GetValues<SimulationScenarioKind>()
        : [Scenario];

    public static SimulationCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimulationScenarioKind scenario = SimulationScenarioKind.Scenario_1_Baseline;
        bool runAll = false;
        bool scenarioSpecified = false;

        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == AllOption)
            {
                runAll = true;
                continue;
            }

            if (args[index] != ScenarioOption)
            {
                throw new ArgumentException($"Unknown option '{args[index]}'. {Usage}", nameof(args));
            }

            if (index + 1 >= args.Count)
            {
                throw new ArgumentException(
                    $"Option '{ScenarioOption}' requires a value. {Usage}",
                    nameof(args));
            }

            scenario = ParseScenario(args[++index]);
            scenarioSpecified = true;
        }

        if (runAll && scenarioSpecified)
        {
            throw new ArgumentException($"Options '{AllOption}' and '{ScenarioOption}' cannot be combined. {Usage}", nameof(args));
        }

        return new SimulationCommandLineOptions(scenario, runAll);
    }

    private static SimulationScenarioKind ParseScenario(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "scenario-0" => SimulationScenarioKind.Scenario_0,
            "scenario-1-baseline" => SimulationScenarioKind.Scenario_1_Baseline,
            "scenario-1-dynamic" => SimulationScenarioKind.Scenario_1_Dynamic,
            "scenario-2-baseline" => SimulationScenarioKind.Scenario_2_Baseline,
            "scenario-2-dynamic" => SimulationScenarioKind.Scenario_2_Dynamic,
            "scenario-3-baseline" => SimulationScenarioKind.Scenario_3_Baseline,
            "scenario-3-dynamic" => SimulationScenarioKind.Scenario_3_Dynamic,
            "scenario-4-baseline" => SimulationScenarioKind.Scenario_4_Baseline,
            "scenario-4-dynamic" => SimulationScenarioKind.Scenario_4_Dynamic,

            _ => throw new ArgumentException($"Unknown scenario '{value}'. {Usage}")
        };
    }
}
