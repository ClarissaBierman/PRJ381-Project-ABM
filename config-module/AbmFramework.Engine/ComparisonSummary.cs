using AbmFramework.Config;

namespace AbmFramework.Engine;

// One column of the Compare table: a single number read from a replicate
// (its final value, e.g. "Final infected") and, where it makes sense, how to
// read the same number at every tick so it can be charted over time.
public sealed class ComparisonColumn
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    // Name used when the value is drawn over time ("Infected" rather than
    // "Final infected"). Falls back to Label.
    public string? SeriesLabel { get; init; }

    // "%" or "" - shown after the value in the UI.
    public string Unit { get; init; } = "";

    // Value at one tick. Null for columns that only exist for a whole run
    // (e.g. peak infected), which therefore get no line on the chart.
    internal Func<TickStatistics, double>? AtTick { get; init; }

    // Value for a whole replicate. Defaults to the value at the last tick.
    internal Func<IReadOnlyList<TickStatistics>, double>? ForRun { get; init; }

    public bool HasSeries => AtTick is not null;

    internal double FinalValue(IReadOnlyList<TickStatistics> history) =>
        ForRun is not null ? ForRun(history) : AtTick!(history[^1]);
}

public static class ComparisonColumns
{
    // The columns that make sense for each model: the "final values" the
    // spec asks for (final infected for SIR, % happy for Schelling, food
    // collected for Ants, sheep and wolves left for Wolf-Sheep, ...).
    public static IReadOnlyList<ComparisonColumn> For(ModelType model) => model switch
    {
        ModelType.SIR => new[]
        {
            Col("Infected", "Final infected", s => s.Infected, seriesLabel: "Infected"),
            Col("Susceptible", "Final susceptible", s => s.Susceptible, seriesLabel: "Susceptible"),
            Col("Recovered", "Final recovered", s => s.Recovered, seriesLabel: "Recovered"),
            new ComparisonColumn
            {
                Key = "PeakInfected",
                Label = "Peak infected",
                ForRun = history => history.Max(s => s.Infected)
            }
        },

        ModelType.Schelling => new[]
        {
            Col("PercentHappy", "% happy", s => Metric(s, "HappyFraction") * 100.0, "%"),
            Col("HappyAgents", "Happy agents", s => Metric(s, "HappyAgents")),
            Col("UnhappyAgents", "Unhappy agents", s => Metric(s, "UnhappyAgents"))
        },

        ModelType.Boids => new[]
        {
            Col("AverageSpeed", "Average speed", s => Metric(s, "AverageSpeed")),
            Col("FlockRadius", "Flock radius", s => Metric(s, "FlockRadius"))
        },

        ModelType.AntForaging => new[]
        {
            Col("FoodCollected", "Food collected", s => Metric(s, "FoodCollected")),
            Col("FoodRemaining", "Food remaining", s => Metric(s, "FoodRemaining")),
            Col("AntsCarryingFood", "Ants carrying food", s => Metric(s, "AntsCarryingFood"))
        },

        ModelType.WolfSheep => new[]
        {
            Col("Sheep", "Sheep left", s => Metric(s, "Sheep"), seriesLabel: "Sheep"),
            Col("Wolves", "Wolves left", s => Metric(s, "Wolves"), seriesLabel: "Wolves"),
            Col("Grass", "Grass left", s => Metric(s, "Grass"), seriesLabel: "Grass")
        },

        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown model.")
    };

    private static ComparisonColumn Col(
        string key, string label, Func<TickStatistics, double> atTick, string unit = "", string? seriesLabel = null) =>
        new() { Key = key, Label = label, SeriesLabel = seriesLabel, Unit = unit, AtTick = atTick };

    private static double Metric(TickStatistics stats, string key) =>
        stats.Metrics.TryGetValue(key, out var value) ? value : 0.0;
}

// One replicate's row in the results table.
public sealed class ComparisonRow
{
    // 1-based, as shown to the user ("Run 7 of 20").
    public required int Run { get; init; }

    public required int Seed { get; init; }

    // One value per column, in the same order as ComparisonSummary.Columns.
    public required double[] Values { get; init; }
}

// Average, lowest, highest and spread across all replicates, for one column.
public sealed class ComparisonColumnStats
{
    public required string Key { get; init; }

    public required double Mean { get; init; }

    public required double Min { get; init; }

    public required double Max { get; init; }

    // Sample standard deviation (n - 1), the usual choice when the runs
    // are a sample of all the runs the model could produce.
    public required double StdDev { get; init; }
}

// The average over time with the lowest and highest as a band around it.
public sealed class ComparisonSeries
{
    public required string Key { get; init; }

    public required int[] Ticks { get; init; }

    public required double[] Mean { get; init; }

    public required double[] Min { get; init; }

    public required double[] Max { get; init; }
}

public sealed class ComparisonSummary
{
    public required string Model { get; init; }

    public required int RunCount { get; init; }

    public required int TickLimit { get; init; }

    // The seed all the per-run seeds were derived from; typing it back into
    // the seed box reproduces the whole comparison.
    public required int BaseSeed { get; init; }

    public required IReadOnlyList<ComparisonColumn> Columns { get; init; }

    public required IReadOnlyList<ComparisonRow> Rows { get; init; }

    public required IReadOnlyList<ComparisonColumnStats> Stats { get; init; }

    public required IReadOnlyList<ComparisonSeries> Series { get; init; }

    // Builds the whole summary from the replicates a MonteCarloRunner
    // produced. Kept free of any web code so it can be unit tested.
    public static ComparisonSummary Build(
        ModelType model,
        int baseSeed,
        IReadOnlyList<MonteCarloReplicateResult> results,
        int maxSeriesPoints = 300)
    {
        if (results.Count == 0)
            throw new ArgumentException("At least one replicate is needed.", nameof(results));

        var columns = ComparisonColumns.For(model);

        var rows = results
            .Select(r => new ComparisonRow
            {
                Run = r.ReplicateIndex + 1,
                Seed = r.Seed,
                Values = columns.Select(c => c.FinalValue(r.History)).ToArray()
            })
            .ToList();

        var stats = columns
            .Select((c, i) => Describe(c.Key, rows.Select(row => row.Values[i]).ToArray()))
            .ToList();

        var series = columns
            .Where(c => c.HasSeries)
            .Select(c => BuildSeries(c, results, maxSeriesPoints))
            .ToList();

        return new ComparisonSummary
        {
            Model = model.ToString(),
            RunCount = results.Count,
            TickLimit = results.Max(r => r.History.Count),
            BaseSeed = baseSeed,
            Columns = columns,
            Rows = rows,
            Stats = stats,
            Series = series
        };
    }

    public static ComparisonColumnStats Describe(string key, IReadOnlyList<double> values)
    {
        double mean = values.Average();

        // With a single value there is no spread to measure.
        double stdDev = values.Count < 2
            ? 0.0
            : Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));

        return new ComparisonColumnStats
        {
            Key = key,
            Mean = mean,
            Min = values.Min(),
            Max = values.Max(),
            StdDev = stdDev
        };
    }

    // Mean/min/max across replicates at each tick. Replicates that stopped
    // early are held at their last value so every tick compares the same
    // number of runs. Long runs are thinned to about maxPoints so the page
    // is not sent (and does not draw) thousands of points per line.
    private static ComparisonSeries BuildSeries(
        ComparisonColumn column,
        IReadOnlyList<MonteCarloReplicateResult> results,
        int maxPoints)
    {
        int length = results.Max(r => r.History.Count);
        int step = Math.Max(1, (int)Math.Ceiling(length / (double)Math.Max(2, maxPoints)));

        // Every step-th tick, plus the final tick so the line always ends
        // where the run ends.
        var indexes = Enumerable.Range(0, length).Where(i => i % step == 0).ToList();
        if (indexes[^1] != length - 1) indexes.Add(length - 1);

        var ticks = new int[indexes.Count];
        var mean = new double[indexes.Count];
        var min = new double[indexes.Count];
        var max = new double[indexes.Count];

        var longest = results.First(r => r.History.Count == length);

        for (int p = 0; p < indexes.Count; p++)
        {
            int index = indexes[p];
            ticks[p] = longest.History[index].Tick;

            var values = results
                .Select(r => column.AtTick!(r.History[Math.Min(index, r.History.Count - 1)]))
                .ToArray();

            mean[p] = values.Average();
            min[p] = values.Min();
            max[p] = values.Max();
        }

        return new ComparisonSeries { Key = column.Key, Ticks = ticks, Mean = mean, Min = min, Max = max };
    }
}
