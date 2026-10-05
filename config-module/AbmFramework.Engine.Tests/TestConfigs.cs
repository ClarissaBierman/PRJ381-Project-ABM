using AbmFramework.Config;

namespace AbmFramework.Engine.Tests;

// Small, fast scenarios for each model. Model parameters are deliberately
// set away from SimulationConfig's defaults, so a test can tell whether a
// setting actually reached the engine rather than silently falling back.
internal static class TestConfigs
{
    public static IEnumerable<object[]> AllModels =>
        Enum.GetValues<ModelType>().Select(m => new object[] { m });

    public static SimulationConfig For(ModelType model, int tickLimit = 30, int? seed = 123) => model switch
    {
        ModelType.SIR => new SimulationConfig
        {
            Model = ModelType.SIR,
            GridWidth = 15,
            GridHeight = 15,
            AgentCount = 120,
            InitialInfected = 4,
            InfectionProbability = 0.4,
            RecoveryTicks = 6,
            TickLimit = tickLimit,
            RandomSeed = seed
        },

        ModelType.Schelling => new SimulationConfig
        {
            Model = ModelType.Schelling,
            GridWidth = 12,
            GridHeight = 12,
            AgentCount = 100,
            SimilarityThreshold = 0.6,
            GroupARatio = 0.4,
            TickLimit = tickLimit,
            RandomSeed = seed
        },

        ModelType.Boids => new SimulationConfig
        {
            Model = ModelType.Boids,
            GridWidth = 15,
            GridHeight = 15,
            Topology = GridTopology.Toroidal,
            AgentCount = 30,
            PerceptionRadius = 3.0,
            SeparationWeight = 1.2,
            AlignmentWeight = 0.8,
            CohesionWeight = 0.6,
            MaxSpeed = 0.9,
            TickLimit = tickLimit,
            RandomSeed = seed
        },

        ModelType.AntForaging => new SimulationConfig
        {
            Model = ModelType.AntForaging,
            GridWidth = 15,
            GridHeight = 15,
            AgentCount = 20,
            FoodSources = 2,
            FoodPerSource = 30,
            PheromoneDepositAmount = 4.0,
            PheromoneDecayRate = 0.05,
            ExplorationChance = 0.2,
            TickLimit = tickLimit,
            RandomSeed = seed
        },

        ModelType.WolfSheep => new SimulationConfig
        {
            Model = ModelType.WolfSheep,
            GridWidth = 15,
            GridHeight = 15,
            Topology = GridTopology.Toroidal,
            InitialSheep = 40,
            InitialWolves = 6,
            InitialSheepEnergy = 6.0,
            InitialWolfEnergy = 10.0,
            EnergyLossPerTick = 0.5,
            GrassEnergyGain = 3.0,
            SheepEnergyGain = 6.0,
            SheepReproductionProbability = 0.1,
            WolfReproductionProbability = 0.08,
            GrassRegrowthProbability = 0.1,
            TickLimit = tickLimit,
            RandomSeed = seed
        },

        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };

    // A compact, exact text form of one tick's statistics, for comparing runs.
    public static string Signature(TickStatistics stats) =>
        $"{stats.Tick}|{stats.Susceptible}|{stats.Infected}|{stats.Recovered}|{stats.TotalPopulation}|" +
        string.Join(";", stats.Metrics.OrderBy(m => m.Key).Select(m => $"{m.Key}={m.Value:R}"));

    public static List<string> Signatures(IEnumerable<TickStatistics> history) =>
        history.Select(Signature).ToList();
}
