// =============================================================================
// ComparisonSummary.cs  (AbmFramework.Engine)
//
// Turns a Monte Carlo batch into everything the Compare panel shows:
//   - Columns: which numbers to show for this model (ComparisonColumns.For).
//   - Rows:    one row per run with its seed and final values.
//   - Stats:   average, lowest, highest and spread (standard deviation) per column.
//   - Series:  the average over time with a lowest-to-highest band, for the chart.
//
// It has no web code in it on purpose, so it can be unit tested on its own
// (see ComparisonSummaryTests). The Dashboard's ComparisonService calls Build
// and sends the result to the browser as JSON.
//
// ADD A MODEL: add a case to ComparisonColumns.For with your model's columns.
// =============================================================================

using AbmFramework.Config;

namespace AbmFramework.Engine;

/// <summary>
/// One column of the Compare table: a single number read from a run (its
/// final value, e.g. "Final infected") and, where it makes sense, how to read
/// the same number at every tick so it can be charted over time.
/// </summary>
public sealed class ComparisonColumn
{
    /// <summary>Short id used in code and JSON, for example "PeakInfected".</summary>
    public required string Key { get; init; }

    /// <summary>Column heading shown to the user, for example "Peak infected".</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Name used when the value is drawn over time ("Infected" rather than
    /// "Final infected"). Falls back to Label.
    /// </summary>
    public string? SeriesLabel { get; init; }

    /// <summary>"%" or "", shown after the value in the UI.</summary>
    public string Unit { get; init; } = "";

    // Value at one tick. Null for columns that only exist for a whole run
    // (e.g. peak infected), which therefore get no line on the chart.
    internal Func<TickStatistics, double>? AtTick { get; init; }

    // Value for a whole run. Defaults to the value at the last tick.
    internal Func<IReadOnlyList<TickStatistics>, double>? ForRun { get; init; }

    /// <summary>True if this column can be drawn as a line over time on the chart.</summary>
    public bool HasSeries => AtTick is not null;

    // The single number this column shows for one run.
    internal double FinalValue(IReadOnlyList<TickStatistics> history) =>
        ForRun is not null ? ForRun(history) : AtTick!(history[^1]);
}

/// <summary>Which columns the Compare panel shows for each model.</summary>
public static class ComparisonColumns
{
    /// <summary>
    /// The columns that make sense for each model: the "final values" the
    /// spec asks for (final infected for SIR, % happy for Schelling, food
    /// collected for Ants, sheep and wolves left for Wolf-Sheep, ...).
    /// ADD A MODEL: add a case here, or the Compare panel throws for it.
    /// </summary>
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

    // Shortcut for the common case: a column that has a value at every tick.
    private static ComparisonColumn Col(
        string key, string label, Func<TickStatistics, double> atTick, string unit = "", string? seriesLabel = null) =>
        new() { Key = key, Label = label, SeriesLabel = seriesLabel, Unit = unit, AtTick = atTick };

    // Reads a named metric, or 0 if this tick didn't report it.
    private static double Metric(TickStatistics stats, string key) =>
        stats.Metrics.TryGetValue(key, out var value) ? value : 0.0;
}

/// <summary>One run's row in the results table.</summary>
public sealed class ComparisonRow
{
    /// <summary>Run number, starting at 1 as shown to the user ("Run 7 of 20").</summary>
    public required int Run { get; init; }

    /// <summary>The seed this run used, so it can be replayed exactly.</summary>
    public required int Seed { get; init; }

    /// <summary>One value per column, in the same order as ComparisonSummary.Columns.</summary>
    public required double[] Values { get; init; }
}

/// <summary>Average, lowest, highest and spread across all runs, for one column.</summary>
public sealed class ComparisonColumnStats
{
    /// <summary>The column this belongs to (matches ComparisonColumn.Key).</summary>
    public required string Key { get; init; }

    /// <summary>Average over all runs.</summary>
    public required double Mean { get; init; }

    /// <summary>Lowest value of any run.</summary>
    public required double Min { get; init; }

    /// <summary>Highest value of any run.</summary>
    public required double Max { get; init; }

    /// <summary>
    /// Sample standard deviation (divides by n - 1), the usual choice when the
    /// runs are a sample of all the runs the model could produce. Shown as "±".
    /// </summary>
    public required double StdDev { get; init; }
}

/// <summary>
/// One chart line: the average over time, with the lowest and highest run as
/// a shaded band around it. All arrays have the same length (one entry per point).
/// </summary>
public sealed class ComparisonSeries
{
    /// <summary>The column this line is for.</summary>
    public required string Key { get; init; }

    /// <summary>The tick number of each point.</summary>
    public required int[] Ticks { get; init; }

    /// <summary>Average over all runs at each point.</summary>
    public required double[] Mean { get; init; }

    /// <summary>Lowest run at each point (bottom of the band).</summary>
    public required double[] Min { get; init; }

    /// <summary>Highest run at each point (top of the band).</summary>
    public required double[] Max { get; init; }
}

/// <summary>Everything the Compare panel shows for one setting (A or B).</summary>
public sealed class ComparisonSummary
{
    /// <summary>The model's name, for example "SIR".</summary>
    public required string Model { get; init; }

    /// <summary>How many runs were done.</summary>
    public required int RunCount { get; init; }

    /// <summary>The number of ticks in the longest run.</summary>
    public required int TickLimit { get; init; }

    /// <summary>
    /// The seed all the per-run seeds were derived from. Typing it back into
    /// the seed box reproduces the whole comparison.
    /// </summary>
    public required int BaseSeed { get; init; }

    /// <summary>The columns shown for this model.</summary>
    public required IReadOnlyList<ComparisonColumn> Columns { get; init; }

    /// <summary>One row per run.</summary>
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }

    /// <summary>Average, lowest, highest and spread for each column.</summary>
    public required IReadOnlyList<ComparisonColumnStats> Stats { get; init; }

    /// <summary>Chart lines, only for columns that have a value at every tick.</summary>
    public required IReadOnlyList<ComparisonSeries> Series { get; init; }

    /// <summary>
    /// Builds the whole summary from the runs a MonteCarloRunner produced.
    /// Kept free of any web code so it can be unit tested.
    /// </summary>
    /// <param name="model">Which model was run (decides the columns).</param>
    /// <param name="baseSeed">The base seed of the batch.</param>
    /// <param name="results">The runs. Must contain at least one.</param>
    /// <param name="maxSeriesPoints">Roughly how many points each chart line may have.</param>
    /// <exception cref="ArgumentException">If results is empty.</exception>
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

    /// <summary>Works out the average, lowest, highest and standard deviation of a list of numbers.</summary>
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

    // Mean/min/max across runs at each tick. Runs that stopped
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
