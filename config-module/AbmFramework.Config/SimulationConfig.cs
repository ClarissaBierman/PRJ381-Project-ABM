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

public enum ModelType
{
    SIR,
    Schelling,
    Boids,
    AntForaging
}

public sealed class SimulationConfig
{
    public string ScenarioName { get; set; } = "Default SIR Scenario";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModelType Model { get; set; } = ModelType.SIR;

    public int GridWidth { get; set; } = 50;

    public int GridHeight { get; set; } = 50;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GridTopology Topology { get; set; } = GridTopology.Bounded;

    public int AgentCount { get; set; } = 200;

    public int? RandomSeed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SchedulerKind Scheduler { get; set; } = SchedulerKind.Random;

    public int TickLimit { get; set; } = 500;

    // --- SIR epidemic parameters ---
    public int InitialInfected { get; set; } = 5;

    public double InfectionProbability { get; set; } = 0.3;

    public int RecoveryTicks { get; set; } = 10;

    // --- Schelling segregation parameters ---
    public double SimilarityThreshold { get; set; } = 0.5;

    // Proportion of agents assigned to group 0 (the rest go to group 1).
    public double GroupARatio { get; set; } = 0.5;

    // --- Boid flocking parameters ---
    public double PerceptionRadius { get; set; } = 2.0;

    public double SeparationWeight { get; set; } = 1.5;

    public double AlignmentWeight { get; set; } = 1.0;

    public double CohesionWeight { get; set; } = 1.0;

    public double MaxSpeed { get; set; } = 1.0;

    // --- Ant foraging parameters ---
    public int FoodSources { get; set; } = 3;

    public int FoodPerSource { get; set; } = 50;

    public double PheromoneDepositAmount { get; set; } = 5.0;

    public double PheromoneDecayRate { get; set; } = 0.02;

    public double ExplorationChance { get; set; } = 0.1;
}
