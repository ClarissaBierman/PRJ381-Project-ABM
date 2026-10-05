using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Engine.Tests;

public class ComparisonSummaryTests
{
    private static TickStatistics Sir(int tick, int infected) =>
        new() { Tick = tick, Infected = infected, Susceptible = 100 - infected, TotalPopulation = 100 };

    private static TickStatistics WithMetrics(int tick, params (string Key, double Value)[] metrics) =>
        new() { Tick = tick, Metrics = metrics.ToDictionary(m => m.Key, m => m.Value) };

    private static MonteCarloReplicateResult Replicate(int index, int seed, IReadOnlyList<TickStatistics> history) =>
        new() { ReplicateIndex = index, Seed = seed, FinalStatistics = history[^1], History = history };

    [Fact]
    public void Describe_GivesMeanLowestHighestAndSampleStandardDeviation()
    {
        var stats = ComparisonSummary.Describe("x", new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });

        Assert.Equal(5.0, stats.Mean, 10);
        Assert.Equal(2.0, stats.Min);
        Assert.Equal(9.0, stats.Max);
        // Sample (n - 1) standard deviation: sqrt(32 / 7).
        Assert.Equal(Math.Sqrt(32.0 / 7.0), stats.StdDev, 10);
    }

    [Fact]
    public void Describe_SingleValue_HasNoSpread()
    {
        var stats = ComparisonSummary.Describe("x", new[] { 3.5 });

        Assert.Equal(3.5, stats.Mean);
        Assert.Equal(0.0, stats.StdDev);
    }

    [Fact]
    public void Build_NoReplicates_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ComparisonSummary.Build(ModelType.SIR, 1, Array.Empty<MonteCarloReplicateResult>()));
    }

    [Fact]
    public void Build_Sir_RowsAreOneBasedAndCarrySeedsAndFinalValues()
    {
        var results = new[]
        {
            Replicate(0, 111, new[] { Sir(0, 5), Sir(1, 30), Sir(2, 10) }),
            Replicate(1, 222, new[] { Sir(0, 5), Sir(1, 50), Sir(2, 20) })
        };

        var summary = ComparisonSummary.Build(ModelType.SIR, 42, results);

        Assert.Equal(2, summary.RunCount);
        Assert.Equal(42, summary.BaseSeed);
        Assert.Equal(new[] { 1, 2 }, summary.Rows.Select(r => r.Run));
        Assert.Equal(new[] { 111, 222 }, summary.Rows.Select(r => r.Seed));

        var columns = summary.Columns.Select(c => c.Key).ToList();
        Assert.Equal(10.0, summary.Rows[0].Values[columns.IndexOf("Infected")]);
        Assert.Equal(20.0, summary.Rows[1].Values[columns.IndexOf("Infected")]);
        Assert.Equal(30.0, summary.Rows[0].Values[columns.IndexOf("PeakInfected")]);
        Assert.Equal(50.0, summary.Rows[1].Values[columns.IndexOf("PeakInfected")]);
    }

    [Fact]
    public void Build_StatsMatchTheRows()
    {
        var results = new[]
        {
            Replicate(0, 1, new[] { Sir(0, 5), Sir(1, 10) }),
            Replicate(1, 2, new[] { Sir(0, 5), Sir(1, 20) }),
            Replicate(2, 3, new[] { Sir(0, 5), Sir(1, 30) })
        };

        var summary = ComparisonSummary.Build(ModelType.SIR, 0, results);
        var infected = summary.Stats.Single(s => s.Key == "Infected");

        Assert.Equal(20.0, infected.Mean);
        Assert.Equal(10.0, infected.Min);
        Assert.Equal(30.0, infected.Max);
        Assert.Equal(10.0, infected.StdDev, 10);
    }

    [Fact]
    public void Build_Schelling_ReportsPercentHappyAsAPercentage()
    {
        var history = new[]
        {
            WithMetrics(0, ("HappyFraction", 0.5), ("HappyAgents", 50), ("UnhappyAgents", 50)),
            WithMetrics(1, ("HappyFraction", 0.85), ("HappyAgents", 85), ("UnhappyAgents", 15))
        };

        var summary = ComparisonSummary.Build(ModelType.Schelling, 0, new[] { Replicate(0, 1, history) });
        var index = summary.Columns.Select(c => c.Key).ToList().IndexOf("PercentHappy");

        Assert.Equal(85.0, summary.Rows[0].Values[index], 10);
        Assert.Equal("%", summary.Columns[index].Unit);
    }

    [Fact]
    public void Build_WolfSheep_ReportsSheepWolvesAndGrassLeft()
    {
        var history = new[] { WithMetrics(0, ("Sheep", 7), ("Wolves", 0), ("Grass", 120)) };

        var summary = ComparisonSummary.Build(ModelType.WolfSheep, 0, new[] { Replicate(0, 1, history) });

        Assert.Equal(new[] { "Sheep", "Wolves", "Grass" }, summary.Columns.Select(c => c.Key));
        Assert.Equal(new[] { 7.0, 0.0, 120.0 }, summary.Rows[0].Values);
    }

    [Theory]
    [MemberData(nameof(TestConfigs.AllModels), MemberType = typeof(TestConfigs))]
    public void ColumnsFor_EveryModelHasColumnsWithUniqueKeys(ModelType model)
    {
        var columns = ComparisonColumns.For(model);

        Assert.NotEmpty(columns);
        Assert.Equal(columns.Count, columns.Select(c => c.Key).Distinct().Count());
        Assert.Contains(columns, c => c.HasSeries);
    }

    [Fact]
    public void Build_Series_AveragesAndBracketsEveryTick()
    {
        var results = new[]
        {
            Replicate(0, 1, new[] { Sir(0, 10), Sir(1, 20) }),
            Replicate(1, 2, new[] { Sir(0, 30), Sir(1, 40) })
        };

        var series = ComparisonSummary.Build(ModelType.SIR, 0, results).Series.Single(s => s.Key == "Infected");

        Assert.Equal(new[] { 0, 1 }, series.Ticks);
        Assert.Equal(new[] { 20.0, 30.0 }, series.Mean);
        Assert.Equal(new[] { 10.0, 20.0 }, series.Min);
        Assert.Equal(new[] { 30.0, 40.0 }, series.Max);
    }

    [Fact]
    public void Build_Series_HoldsAShorterRunAtItsLastValue()
    {
        var results = new[]
        {
            Replicate(0, 1, new[] { Sir(0, 10), Sir(1, 10), Sir(2, 10) }),
            Replicate(1, 2, new[] { Sir(0, 0), Sir(1, 20) })
        };

        var series = ComparisonSummary.Build(ModelType.SIR, 0, results).Series.Single(s => s.Key == "Infected");

        // At tick 2 the second run has ended, so it counts as 20, not 0.
        Assert.Equal(15.0, series.Mean[2]);
        Assert.Equal(10.0, series.Min[2]);
        Assert.Equal(20.0, series.Max[2]);
    }

    [Fact]
    public void Build_Series_IsThinnedButAlwaysEndsOnTheFinalTick()
    {
        var history = Enumerable.Range(0, 1000).Select(t => Sir(t, t % 50)).ToList();
        var results = new[] { Replicate(0, 1, history), Replicate(1, 2, history) };

        var series = ComparisonSummary.Build(ModelType.SIR, 0, results, maxSeriesPoints: 100)
            .Series.Single(s => s.Key == "Infected");

        Assert.InRange(series.Ticks.Length, 2, 101);
        Assert.Equal(0, series.Ticks[0]);
        Assert.Equal(999, series.Ticks[^1]);
        Assert.Equal(series.Ticks.Length, series.Mean.Length);
    }

    [Fact]
    public void Build_PeakInfected_HasNoSeries()
    {
        var results = new[] { Replicate(0, 1, new[] { Sir(0, 5), Sir(1, 9) }) };

        var summary = ComparisonSummary.Build(ModelType.SIR, 0, results);

        Assert.DoesNotContain(summary.Series, s => s.Key == "PeakInfected");
        Assert.Contains(summary.Columns, c => c.Key == "PeakInfected" && !c.HasSeries);
    }

    [Theory]
    [MemberData(nameof(TestConfigs.AllModels), MemberType = typeof(TestConfigs))]
    public async Task Build_FromARealBatch_HasOneRowPerRunAndOnePointPerTick(ModelType model)
    {
        var config = TestConfigs.For(model, tickLimit: 20);

        var results = await new MonteCarloRunner().RunAsync(config, 3, baseSeed: 5);
        var summary = ComparisonSummary.Build(model, 5, results);

        Assert.Equal(3, summary.Rows.Count);
        Assert.All(summary.Rows, r => Assert.Equal(summary.Columns.Count, r.Values.Length));
        Assert.All(summary.Series, s => Assert.Equal(20, s.Ticks.Length));
        Assert.All(summary.Stats, s => Assert.InRange(s.Mean, s.Min, s.Max));
    }

    [Fact]
    public async Task RunAsync_ReportsHowManyRunsHaveFinished()
    {
        var reported = new List<int>();
        var progress = new SynchronousProgress(reported.Add);

        await new MonteCarloRunner().RunAsync(
            TestConfigs.For(ModelType.SIR, tickLimit: 5), 4, baseSeed: 1, progress: progress);

        Assert.Equal(new[] { 1, 2, 3, 4 }, reported);
    }

    // Progress<T> posts to the thread pool, which can reorder updates; this
    // one reports on the calling thread so the test can check the order.
    private sealed class SynchronousProgress : IProgress<int>
    {
        private readonly Action<int> _callback;

        public SynchronousProgress(Action<int> callback) => _callback = callback;

        public void Report(int value) => _callback(value);
    }
}
