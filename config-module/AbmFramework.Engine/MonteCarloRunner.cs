// =============================================================================
// MonteCarloRunner.cs  (AbmFramework.Engine)
//
// Monte Carlo = run the same scenario many times, each with a different random
// seed, and look at the spread of results. One run of a random model can be a
// fluke. A hundred runs show what usually happens and how much it varies.
//
// The seeds are not picked at random from the clock. They come from ONE base
// seed, so the whole batch (all 100 runs) can be repeated exactly by using the
// same base seed again. The dashboard's Compare panel is built on this class.
// =============================================================================

using AbmFramework.Config;

namespace AbmFramework.Engine;

/// <summary>The result of one run inside a Monte Carlo batch.</summary>
public sealed class MonteCarloReplicateResult
{
    /// <summary>Position of this run in the batch, starting at 0.</summary>
    public required int ReplicateIndex { get; init; }

    /// <summary>The seed this run used. Put it in a config to replay exactly this run.</summary>
    public required int Seed { get; init; }

    /// <summary>The statistics of the last tick.</summary>
    public required TickStatistics FinalStatistics { get; init; }

    /// <summary>The statistics of every tick, in order.</summary>
    public required IReadOnlyList<TickStatistics> History { get; init; }
}

/// <summary>
/// Runs the same scenario several times with different seeds (all derived
/// from one base seed, so the batch is repeatable) and collects every run's results.
/// </summary>
public sealed class MonteCarloRunner
{
    /// <summary>Runs the batch one run after another and returns all results.</summary>
    /// <param name="baseConfig">The scenario. It is copied for each run, so it is never changed.</param>
    /// <param name="replicateCount">How many runs to do. Must be at least 1.</param>
    /// <param name="baseSeed">Seed that generates every run's seed. Null = a different batch every time.</param>
    /// <param name="cancellationToken">Cancel to stop the batch (the Compare panel's Cancel button).</param>
    /// <param name="progress">Optional. Told how many runs have finished after each one.</param>
    /// <returns>One result per run, in run order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If replicateCount is 0 or less.</exception>
    /// <exception cref="OperationCanceledException">If the batch is cancelled.</exception>
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

            // Copy the settings so each run gets its own seed and nothing
            // leaks from one run into the next.
            var replicateConfig = baseConfig.Clone();
            replicateConfig.RandomSeed = seed;

            // A brand new engine per run, for the same reason.
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
