namespace Simulation;

using System;
using System.Collections.Generic;

public enum SimulationScenarioKind
{
    Scenario_1_Baseline,
    Scenario_1_Dynamic,

    Scenario_2_Baseline,
    Scenario_2_Dynamic,
    
    Scenario_3_Baseline,
    Scenario_3_Dynamic,
    
    Scenario_4_Baseline,
    Scenario_4_Dynamic,
}

public sealed record SimulationCommandLineOptions(SimulationScenarioKind Scenario)
{
    private const string ScenarioOption = "--scenario";

    public static string Usage =>
        "Usage: Simulation [--scenario baseline | dynamic]";

    public static SimulationCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimulationScenarioKind scenario = SimulationScenarioKind.Scenario_1_Baseline;

        for (var index = 0; index < args.Count; index++)
        {
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
        }

        return new SimulationCommandLineOptions(scenario);
    }

    private static SimulationScenarioKind ParseScenario(string value)
    {
        return value.ToLowerInvariant() switch
        {
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
