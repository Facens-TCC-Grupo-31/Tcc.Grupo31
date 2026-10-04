namespace Simulation;

using System;
using System.Collections.Generic;

public enum SimulationScenarioKind
{
    Baseline,
    Dynamic,
}

public sealed record SimulationCommandLineOptions(SimulationScenarioKind Scenario)
{
    private const string ScenarioOption = "--scenario";

    public static string Usage =>
        "Usage: Simulation [--scenario baseline | dynamic]";

    public static SimulationCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimulationScenarioKind scenario = SimulationScenarioKind.Baseline;

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
            "baseline" => SimulationScenarioKind.Baseline,
            "dynamic" => SimulationScenarioKind.Dynamic,
            _ => throw new ArgumentException($"Unknown scenario '{value}'. {Usage}")
        };
    }
}
