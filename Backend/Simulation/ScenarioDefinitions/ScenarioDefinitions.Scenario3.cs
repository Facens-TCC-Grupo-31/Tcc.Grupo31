using System.Collections.Frozen;

namespace Simulation.ScenarioDefinitions;

public partial class ScenarioDefinitions
{
    public static class Scenario3
    {
        private static readonly FrozenSet<SimulationSensorDefinition> _sensors = FrozenSet.Create(
        [
            new SimulationSensorDefinition(1 , TimeSpan.FromDays(1), new(-47.467744959344934, -23.562021406992248)),
            new SimulationSensorDefinition(2 , TimeSpan.FromDays(2), new(-47.468328390691354, -23.563215627719043)),
            new SimulationSensorDefinition(3 , TimeSpan.FromDays(1), new(-47.46891650556947 , -23.564610190579227)),
            new SimulationSensorDefinition(4 , TimeSpan.FromDays(2), new(-47.469363984281074, -23.565840674926324)),
            new SimulationSensorDefinition(5 , TimeSpan.FromDays(1), new(-47.46905714173597 , -23.56664927264991 )),
            new SimulationSensorDefinition(6 , TimeSpan.FromDays(2), new(-47.4686480183425  , -23.56585239376946 )),
            new SimulationSensorDefinition(7 , TimeSpan.FromDays(1), new(-47.46822610984299 , -23.56495003978906 )),
            new SimulationSensorDefinition(8 , TimeSpan.FromDays(2), new(-47.46767635028301 , -23.56543051475797 )),
            new SimulationSensorDefinition(9 , TimeSpan.FromDays(1), new(-47.4673695077379  , -23.565840674926324)),
            new SimulationSensorDefinition(10, TimeSpan.FromDays(2), new(-47.46655126095096 , -23.56527816922639 )),
            new SimulationSensorDefinition(11, TimeSpan.FromDays(1), new(-47.467318367313716, -23.564434406160164)),
            new SimulationSensorDefinition(12, TimeSpan.FromDays(2), new(-47.466973169450476, -23.563555480536607)),
            new SimulationSensorDefinition(13, TimeSpan.FromDays(1), new(-47.467625209858824, -23.563450009066617)),
            new SimulationSensorDefinition(14, TimeSpan.FromDays(2), new(-47.465835295012376, -23.562910931342046)),
            new SimulationSensorDefinition(15, TimeSpan.FromDays(1), new(-47.466615186481185, -23.562301536468052)),
            new SimulationSensorDefinition(16, TimeSpan.FromDays(2), new(-47.46540060140682 , -23.56218434482207 )),
            new SimulationSensorDefinition(17, TimeSpan.FromDays(1), new(-47.46455678440778 , -23.562817178468496)),
            new SimulationSensorDefinition(18, TimeSpan.FromDays(2), new(-47.463994239741766, -23.562512481167385)),
            new SimulationSensorDefinition(19, TimeSpan.FromDays(1), new(-47.46394648717784 , -23.563113549477695)),
            new SimulationSensorDefinition(20, TimeSpan.FromDays(2), new(-47.46400386773392 , -23.564050843743697)),
        ]);

        private static readonly IReadOnlyList<long> BaselineScenarioFixedRouteSensorIds = _sensors.Select(x => x.SensorId).ToList();

        public static readonly SimulationScenarioDefinition Baseline = new(
            new SimulationGraphDefinition(OsmPath),
            Timeline: new SimulationScenarioTimelineDefinition(
                SimulationTimespan: TimeSpan.FromDays(7),
                TickInterval: TimeSpan.FromHours(1)
            )
            {
                StartTimeUtc = new DateTime(2026, 09, 10, 12, 0, 0, DateTimeKind.Utc)
            },
            TriggerPolicy: new ScheduledWeekdaysCollectionTriggerPolicy(
                timeOfDay: new TimeOnly(12, 0),
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday
            ),
            Sensors: _sensors.ToList(),
            Route: new SimulationRouteDefinition(_baselineScenarioFixedRoute, BaselineScenarioFixedRouteSensorIds)
        );

        public static SimulationScenarioDefinition Dynamic => Baseline with { Route = null };
    }
}
