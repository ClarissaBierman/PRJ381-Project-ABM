using AbmFramework.Config;

namespace AbmFramework.Engine;

public sealed class MonteCarloReplicateResult
{
    public required int ReplicateIndex { get; init; }
    public required int Seed { get; init; }
    public required TickStatistics FinalStatistics { get; init; }
    public required IReadOnlyList<TickStatistics> History { get; init; }
}

// Runs the same scenario multiple times with different random seeds
// (reproducibly, from a single base seed) and collects each replicate's
// results, so results can be compared/averaged for statistically valid
// conclusions rather than relying on one run.
public sealed class MonteCarloRunner
{
    public async Task<IReadOnlyList<MonteCarloReplicateResult>> RunAsync(
        SimulationConfig baseConfig,
        int replicateCount,
        int? baseSeed = null,
        CancellationToken cancellationToken = default)
    {
        if (replicateCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(replicateCount), "replicateCount must be positive.");

        var results = new List<MonteCarloReplicateResult>();

        // Seeds for each replicate are themselves derived from a single
        // seed, so the whole batch is reproducible end to end.
        var seedGenerator = baseSeed.HasValue ? new Random(baseSeed.Value) : new Random();

        for (int i = 0; i < replicateCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int seed = seedGenerator.Next();

            var replicateConfig = Clone(baseConfig);
            replicateConfig.RandomSeed = seed;

            var engine = new SimulationEngine();
            var history = await engine.RunToCompletionAsync(replicateConfig, cancellationToken);

            results.Add(new MonteCarloReplicateResult
            {
                ReplicateIndex = i,
                Seed = seed,
                FinalStatistics = history[^1],
                History = history
            });
        }

        return results;
    }

    private static SimulationConfig Clone(SimulationConfig config)
    {
        // SimulationConfig only holds value types and strings, so a
        // property-by-property copy is a safe, independent clone.
        return new SimulationConfig
        {
            ScenarioName = config.ScenarioName,
            Model = config.Model,
            GridWidth = config.GridWidth,
            GridHeight = config.GridHeight,
            Topology = config.Topology,
            AgentCount = config.AgentCount,
            RandomSeed = config.RandomSeed,
            Scheduler = config.Scheduler,
            TickLimit = config.TickLimit,
            InitialInfected = config.InitialInfected,
            InfectionProbability = config.InfectionProbability,
            RecoveryTicks = config.RecoveryTicks,
            SimilarityThreshold = config.SimilarityThreshold,
            GroupARatio = config.GroupARatio,
            PerceptionRadius = config.PerceptionRadius,
            SeparationWeight = config.SeparationWeight,
            AlignmentWeight = config.AlignmentWeight,
            CohesionWeight = config.CohesionWeight,
            MaxSpeed = config.MaxSpeed,
            FoodSources = config.FoodSources,
            FoodPerSource = config.FoodPerSource,
            PheromoneDepositAmount = config.PheromoneDepositAmount,
            PheromoneDecayRate = config.PheromoneDecayRate,
            ExplorationChance = config.ExplorationChance
        };
    }
}
