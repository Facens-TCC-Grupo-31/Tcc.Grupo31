namespace Simulation.ScenarioDefinitions;

public partial class ScenarioDefinitions
{
    public static readonly SimulationScenarioDefinition Dynamic = new(
        new SimulationGraphDefinition(OsmPath),
        TickInterval: TimeSpan.FromHours(1),
        TickCount: (int)TimeSpan.FromDays(10).TotalHours,
        TriggerPolicy: new AverageFillThresholdCollectionTriggerPolicy(0.8f),
        Sensors:
        [
            new SimulationSensorDefinition(SensorId: 0 , Position: new(-23.562502, -47.463019)),
            new SimulationSensorDefinition(SensorId: 1 , Position: new(-23.562551, -47.463942)),
            new SimulationSensorDefinition(SensorId: 2 , Position: new(-23.562158, -47.465749)),
            new SimulationSensorDefinition(SensorId: 3 , Position: new(-23.562620, -47.466978)),
            new SimulationSensorDefinition(SensorId: 4 , Position: new(-23.561922, -47.467777)),
            new SimulationSensorDefinition(SensorId: 5 , Position: new(-23.563249, -47.468308)),
            new SimulationSensorDefinition(SensorId: 6 , Position: new(-23.564592, -47.468829)),
            new SimulationSensorDefinition(SensorId: 7 , Position: new(-23.566366, -47.469794)),
            new SimulationSensorDefinition(SensorId: 8 , Position: new(-23.565599, -47.468539)),
            new SimulationSensorDefinition(SensorId: 9 , Position: new(-23.566096, -47.467804)),
            new SimulationSensorDefinition(SensorId: 10, Position: new(-23.565442, -47.465959)),
            new SimulationSensorDefinition(SensorId: 11, Position: new(-23.564936, -47.467439)),
            new SimulationSensorDefinition(SensorId: 12, Position: new(-23.563372, -47.467702)),
            new SimulationSensorDefinition(SensorId: 13, Position: new(-23.563544, -47.467085)),
            new SimulationSensorDefinition(SensorId: 14, Position: new(-23.562797, -47.465578)),
            new SimulationSensorDefinition(SensorId: 15, Position: new(-23.563672, -47.464558)),
            new SimulationSensorDefinition(SensorId: 16, Position: new(-23.563805, -47.463931)),
        ]
    );
}
