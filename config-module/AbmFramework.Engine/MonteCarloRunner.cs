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
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
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

            var replicateConfig = baseConfig.Clone();
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

            // Reports how many replicates have finished so far, so a caller
            // (e.g. the dashboard's Compare progress bar) can show
            // "Run 7 of 20" without waiting for the whole batch.
            progress?.Report(results.Count);
        }

        return results;
    }
}
