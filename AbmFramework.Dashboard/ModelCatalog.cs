using AbmFramework.Config;
using AbmFramework.Dashboard.Models;

namespace AbmFramework.Dashboard;

public static class ModelCatalog
{
    public static IReadOnlyList<ModelOption> Options { get; } = Enum.GetValues<ModelType>()
        .Select(m => new ModelOption
        {
            Model = m.ToString(),
            DisplayName = DisplayName(m),
            Description = Description(m)
        })
        .ToList();

    public static string DisplayName(ModelType model) => model switch
    {
        ModelType.SIR => "SIR Epidemic",
        ModelType.Schelling => "Schelling Segregation",
        ModelType.Boids => "Boids Flocking",
        ModelType.AntForaging => "Ant Foraging",
        _ => model.ToString()
    };

    public static string Description(ModelType model) => model switch
    {
        ModelType.SIR =>
            "Agents move from Susceptible to Infected to Recovered as the infection spreads through neighbouring cells.",
        ModelType.Schelling =>
            "Each agent relocates when too few of its neighbours belong to its own group, which gradually produces segregated clusters.",
        ModelType.Boids =>
            "Each boid follows three local rules (separation, alignment and cohesion), and flocking emerges from them.",
        ModelType.AntForaging =>
            "Ants search for food, then lay pheromone trails back to the nest that other ants follow.",
        _ => string.Empty
    };

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
