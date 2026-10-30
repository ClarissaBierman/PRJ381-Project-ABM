// =============================================================================
// SimulationConfig.cs  (AbmFramework.Config)
//
// Every setting a simulation run needs, for every model, in one class.
// A run only reads the settings for its own Model (for example Wolf-Sheep
// ignores InfectionProbability), so unused settings can be left at their defaults.
//
// Where configs come from:
//   - The dashboard builds one from the sliders (see ModelCatalog in the Dashboard).
//   - ConfigLoader reads one from a .json or .yaml file (see config-module/configs).
// Either way, call ConfigLoader.Validate(config) before running it.
//
// The property names are also the names used in the JSON/YAML files, so
// renaming a property breaks existing scenario files and saved runs.
//
// This project deliberately has no reference to ABM.Core, which is why it
// has its own GridTopology enum (the Engine translates it).
// =============================================================================

using System.Text.Json.Serialization;

namespace AbmFramework.Config;

/// <summary>How the grid treats its edges. Mirrors ABM.Core.GridTopology.</summary>
public enum GridTopology
{
    /// <summary>Edges are walls.</summary>
    Bounded,

    /// <summary>Edges wrap around to the opposite side.</summary>
    Toroidal
}

/// <summary>The order agents take their turns in. See the Engine's Scheduler folder.</summary>
public enum SchedulerKind
{
    /// <summary>Same order every tick (creation order).</summary>
    Sequential,

    /// <summary>A new random order every tick (like NetLogo).</summary>
    Random
}

/// <summary>
/// The models the framework can run.
/// ADD A MODEL: add it here first. See docs/ADDING-A-MODEL.md.
/// </summary>
public enum ModelType
{
    SIR,
    Schelling,
    Boids,
    AntForaging,
    WolfSheep
}

/// <summary>All the settings for one simulation run.</summary>
public sealed class SimulationConfig
{
    // ---- General settings (every model) ------------------------------------

    /// <summary>A name for the scenario, shown in Past runs and exports.</summary>
    public string ScenarioName { get; set; } = "Default SIR Scenario";

    /// <summary>Which model to run. Stored as text ("WolfSheep") in JSON files.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModelType Model { get; set; } = ModelType.SIR;

    /// <summary>Number of grid columns.</summary>
    public int GridWidth { get; set; } = 50;

    /// <summary>Number of grid rows.</summary>
    public int GridHeight { get; set; } = 50;

    /// <summary>Whether the grid edges wrap around (Toroidal) or are walls (Bounded).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GridTopology Topology { get; set; } = GridTopology.Bounded;

    /// <summary>Number of agents for SIR, Schelling, Boids and Ant Foraging. Wolf-Sheep uses InitialSheep and InitialWolves instead.</summary>
    public int AgentCount { get; set; } = 200;

    /// <summary>
    /// Fixes all the randomness in the run. The same seed with the same
    /// settings always gives exactly the same run. Null = different every time.
    /// </summary>
    public int? RandomSeed { get; set; }

    /// <summary>The order agents act in each tick.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SchedulerKind Scheduler { get; set; } = SchedulerKind.Random;

    /// <summary>How many ticks the run lasts.</summary>
    public int TickLimit { get; set; } = 500;

    // ---- SIR epidemic ------------------------------------------------------

    /// <summary>How many people are infected at the start.</summary>
    public int InitialInfected { get; set; } = 5;

    /// <summary>Chance (0 to 1) of catching it from each infected neighbour per tick.</summary>
    public double InfectionProbability { get; set; } = 0.3;

    /// <summary>Ticks someone stays infected before recovering.</summary>
    public int RecoveryTicks { get; set; } = 10;

    // ---- Schelling segregation --------------------------------------------

    /// <summary>Fraction (0 to 1) of neighbours that must be in the agent's own group for it to be happy.</summary>
    public double SimilarityThreshold { get; set; } = 0.5;

    /// <summary>Proportion (0 to 1) of agents assigned to group 0. The rest go to group 1.</summary>
    public double GroupARatio { get; set; } = 0.5;

    // ---- Boids flocking ----------------------------------------------------

    /// <summary>How far a boid can see, in cells.</summary>
    public double PerceptionRadius { get; set; } = 2.0;

    /// <summary>Strength of the "don't crash" rule.</summary>
    public double SeparationWeight { get; set; } = 1.5;

    /// <summary>Strength of the "fly the same way" rule.</summary>
    public double AlignmentWeight { get; set; } = 1.0;

    /// <summary>Strength of the "stay with the group" rule.</summary>
    public double CohesionWeight { get; set; } = 1.0;

    /// <summary>Top speed in cells per tick.</summary>
    public double MaxSpeed { get; set; } = 1.0;

    // ---- Ant foraging ------------------------------------------------------

    /// <summary>Number of food piles.</summary>
    public int FoodSources { get; set; } = 3;

    /// <summary>Units of food in each pile (spread over a 3 x 3 block).</summary>
    public int FoodPerSource { get; set; } = 50;

    /// <summary>Pheromone dropped on each cell by an ant carrying food home.</summary>
    public double PheromoneDepositAmount { get; set; } = 5.0;

    /// <summary>Fraction (0 to 1) of pheromone that fades away every tick.</summary>
    public double PheromoneDecayRate { get; set; } = 0.02;

    /// <summary>Chance (0 to 1) per step that a searching ant ignores the scent and moves randomly.</summary>
    public double ExplorationChance { get; set; } = 0.1;

    // ---- Wolf-Sheep predation ---------------------------------------------

    /// <summary>Number of sheep at the start.</summary>
    public int InitialSheep { get; set; } = 80;

    /// <summary>Number of wolves at the start.</summary>
    public int InitialWolves { get; set; } = 20;

    /// <summary>Energy each sheep starts with.</summary>
    public double InitialSheepEnergy { get; set; } = 4.0;

    /// <summary>Energy each wolf starts with.</summary>
    public double InitialWolfEnergy { get; set; } = 8.0;

    /// <summary>Energy every animal loses per tick.</summary>
    public double EnergyLossPerTick { get; set; } = 1.0;

    /// <summary>Energy a sheep gains from eating one cell of grass.</summary>
    public double GrassEnergyGain { get; set; } = 4.0;

    /// <summary>Energy a wolf gains from eating one sheep.</summary>
    public double SheepEnergyGain { get; set; } = 8.0;

    /// <summary>Chance (0 to 1) per tick that a sheep has a lamb.</summary>
    public double SheepReproductionProbability { get; set; } = 0.04;

    /// <summary>Chance (0 to 1) per tick that a wolf has a pup.</summary>
    public double WolfReproductionProbability { get; set; } = 0.05;

    /// <summary>Chance (0 to 1) per tick that an eaten cell grows grass again.</summary>
    public double GrassRegrowthProbability { get; set; } = 0.03;

    /// <summary>
    /// Returns an independent copy, e.g. for a Monte Carlo run that only
    /// changes the seed. Every property is a value type or an immutable
    /// string, so a shallow copy is a full one, and new properties are
    /// picked up automatically rather than needing to be listed by hand.
    /// (If you ever add a list or other object property, this needs updating.)
    /// </summary>
    public SimulationConfig Clone() => (SimulationConfig)MemberwiseClone();
}
