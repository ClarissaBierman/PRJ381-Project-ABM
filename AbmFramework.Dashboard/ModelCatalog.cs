// =============================================================================
// ModelCatalog.cs  (AbmFramework.Dashboard)
//
// The dashboard's list of models: the name and description shown on the page,
// and the default settings each model starts with when you pick it.
//
// Presets (like Wolf-Sheep's "Stable populations") are NOT here, they live in
// the page (wwwroot/index.html, the "presets" object).
//
// ADD A MODEL: add a DisplayName, a Description and a BuildConfig case.
// =============================================================================

using AbmFramework.Config;
using AbmFramework.Dashboard.Models;

namespace AbmFramework.Dashboard;

/// <summary>Names, descriptions and default settings of every model, for the dashboard.</summary>
public static class ModelCatalog
{
    /// <summary>Every model, in ModelType order, for the model drop-down.</summary>
    public static IReadOnlyList<ModelOption> Options { get; } = Enum.GetValues<ModelType>()
        .Select(m => new ModelOption
        {
            Model = m.ToString(),
            DisplayName = DisplayName(m),
            Description = Description(m)
        })
        .ToList();

    /// <summary>The friendly name shown on the page, e.g. "Wolf-Sheep Predation".</summary>
    public static string DisplayName(ModelType model) => model switch
    {
        ModelType.SIR => "SIR Epidemic",
        ModelType.Schelling => "Schelling Segregation",
        ModelType.Boids => "Boids Flocking",
        ModelType.AntForaging => "Ant Foraging",
        ModelType.WolfSheep => "Wolf-Sheep Predation",
        _ => model.ToString()
    };

    /// <summary>The one-sentence explanation shown under the model's name.</summary>
    public static string Description(ModelType model) => model switch
    {
        ModelType.SIR =>
            "Agents move from susceptible to infected to recovered as the infection spreads through neighbouring cells.",
        ModelType.Schelling =>
            "Each agent relocates when too few of its neighbours belong to its own group, which gradually produces segregated clusters.",
        ModelType.Boids =>
            "Each boid follows three local rules (separation, alignment and cohesion), and flocking emerges from them.",
        ModelType.AntForaging =>
            "Ants search for food, then lay pheromone trails back to the nest that other ants follow.",
        ModelType.WolfSheep =>
            "Sheep graze on regrowing grass while wolves hunt sheep; both populations spend energy, reproduce, and die when depleted.",
        _ => string.Empty
    };

    /// <summary>
    /// The settings a model starts with on the dashboard. These fill in the
    /// sliders when the model is picked. Any setting not listed keeps the
    /// default from SimulationConfig.
    /// </summary>
    public static SimulationConfig BuildConfig(ModelType model) => model switch
    {
        ModelType.Schelling => new SimulationConfig
        {
            ScenarioName = DisplayName(model),
            Model = ModelType.Schelling,
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Bounded,
            AgentCount = 280,
            SimilarityThreshold = 0.7,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        },

        ModelType.Boids => new SimulationConfig
        {
            ScenarioName = DisplayName(model),
            Model = ModelType.Boids,
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Toroidal,
            AgentCount = 60,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        },

        ModelType.AntForaging => new SimulationConfig
        {
            ScenarioName = DisplayName(model),
            Model = ModelType.AntForaging,
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Bounded,
            AgentCount = 30,
            FoodSources = 3,
            FoodPerSource = 50,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        },

        ModelType.WolfSheep => new SimulationConfig
        {
            ScenarioName = DisplayName(model),
            Model = ModelType.WolfSheep,
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Toroidal,
            AgentCount = 100,
            InitialSheep = 80,
            InitialWolves = 20,
            GrassRegrowthProbability = 0.05,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        },

        // SIR (and the fallback for anything unknown).
        _ => new SimulationConfig
        {
            ScenarioName = DisplayName(ModelType.SIR),
            Model = ModelType.SIR,
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Bounded,
            AgentCount = 200,
            InitialInfected = 5,
            InfectionProbability = 0.02,
            RecoveryTicks = 50,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        }
    };
}
