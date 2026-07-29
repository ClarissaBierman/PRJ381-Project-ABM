using System.Text.Json.Serialization;

namespace AbmFramework.Config;

public enum GridTopology
{
    Bounded,
    Toroidal
}

public enum SchedulerKind
{
    Sequential,
    Random
}

public sealed class SimulationConfig
{
    public string ScenarioName { get; set; } = "Default SIR Scenario";

    public int GridWidth { get; set; } = 50;

    public int GridHeight { get; set; } = 50;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GridTopology Topology { get; set; } = GridTopology.Bounded;

    public int AgentCount { get; set; } = 200;

    public int InitialInfected { get; set; } = 5;

    public double InfectionProbability { get; set; } = 0.3;

    public int RecoveryTicks { get; set; } = 10;

    public int TickLimit { get; set; } = 500;

    public int? RandomSeed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SchedulerKind Scheduler { get; set; } = SchedulerKind.Random;
}
