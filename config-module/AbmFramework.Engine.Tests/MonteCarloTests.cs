using System.Globalization;
using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Engine.Tests;

public class MonteCarloRunnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task RunAsync_NonPositiveReplicateCount_Throws(int replicateCount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new MonteCarloRunner().RunAsync(TestConfigs.For(ModelType.SIR), replicateCount));
    }

    [Fact]
    public async Task RunAsync_ReturnsOneCompleteResultPerReplicate()
    {
        var results = await new MonteCarloRunner().RunAsync(TestConfigs.For(ModelType.SIR, tickLimit: 15), 4, baseSeed: 1);

        Assert.Equal(new[] { 0, 1, 2, 3 }, results.Select(r => r.ReplicateIndex));
        Assert.Equal(4, results.Select(r => r.Seed).Distinct().Count());
        Assert.All(results, r =>
        {
            Assert.Equal(15, r.History.Count);
            Assert.Same(r.History[^1], r.FinalStatistics);
        });
    }

    [Fact]
    public async Task RunAsync_SameBaseSeed_ReproducesTheWholeBatch()
    {
        var config = TestConfigs.For(ModelType.Schelling, tickLimit: 15);

        var first = await new MonteCarloRunner().RunAsync(config, 3, baseSeed: 42);
        var second = await new MonteCarloRunner().RunAsync(config, 3, baseSeed: 42);

        Assert.Equal(first.Select(r => r.Seed), second.Select(r => r.Seed));
        Assert.Equal(
            first.Select(r => TestConfigs.Signature(r.FinalStatistics)),
            second.Select(r => TestConfigs.Signature(r.FinalStatistics)));
    }

    [Fact]
    public async Task RunAsync_DoesNotChangeTheBaseConfig()
    {
        var config = TestConfigs.For(ModelType.SIR, tickLimit: 5, seed: 999);

        await new MonteCarloRunner().RunAsync(config, 2, baseSeed: 1);

        Assert.Equal(999, config.RandomSeed);
    }

    // Each replicate must run the scenario exactly as configured, with only
    // the seed changed: re-running a plain engine with the full config and
    // the replicate's seed has to give the same history. This catches any
    // setting the runner fails to copy into a replicate.
    [Theory]
    [MemberData(nameof(TestConfigs.AllModels), MemberType = typeof(TestConfigs))]
    public async Task EachReplicate_MatchesAPlainRunOfTheScenarioWithItsSeed(ModelType model)
    {
        var config = TestConfigs.For(model, tickLimit: 20);

        var replicate = (await new MonteCarloRunner().RunAsync(config, 1, baseSeed: 3))[0];

        config.RandomSeed = replicate.Seed;
        var expected = await new SimulationEngine().RunToCompletionAsync(config);
        Assert.Equal(TestConfigs.Signatures(expected), TestConfigs.Signatures(replicate.History));
    }

    [Fact]
    public async Task WolfSheepReplicates_UseTheScenariosAnimalCounts()
    {
        var config = TestConfigs.For(ModelType.WolfSheep, tickLimit: 1);
        config.InitialSheep = 10;
        config.InitialWolves = 0;
        config.InitialSheepEnergy = 100;
        config.SheepReproductionProbability = 0;

        var replicate = (await new MonteCarloRunner().RunAsync(config, 1, baseSeed: 3))[0];

        Assert.Equal(10, replicate.FinalStatistics.Metrics["Sheep"]);
        Assert.Equal(0, replicate.FinalStatistics.Metrics["Wolves"]);
    }
}

public class MonteCarloCsvExporterTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"mc-test-{Guid.NewGuid()}.csv");

    public void Dispose() => File.Delete(path);

    [Fact]
    public async Task Export_WritesHeaderAndOneAlignedRowPerReplicate()
    {
        var results = await new MonteCarloRunner().RunAsync(TestConfigs.For(ModelType.Schelling, tickLimit: 5), 3, baseSeed: 1);

        await MonteCarloCsvExporter.ExportAsync(results, path);
        var lines = File.ReadAllLines(path);

        Assert.Equal(
            "ReplicateIndex,Seed,FinalTick,Susceptible,Infected,Recovered,TotalPopulation,HappyAgents,HappyFraction,UnhappyAgents",
            lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.All(lines, l => Assert.Equal(10, l.Split(',').Length));
    }

    [Fact]
    public async Task Export_OnDecimalCommaLocale_StillWritesDecimalPoints()
    {
        var results = await new MonteCarloRunner().RunAsync(TestConfigs.For(ModelType.Boids, tickLimit: 5), 2, baseSeed: 1);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-ZA"); // uses ',' as decimal separator
            await MonteCarloCsvExporter.ExportAsync(results, path);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        var lines = File.ReadAllLines(path);
        var header = lines[0].Split(',');
        Assert.All(lines, l => Assert.Equal(header.Length, l.Split(',').Length));
        Assert.Matches(@"^\d+\.\d{2}$", lines[1].Split(',')[Array.IndexOf(header, "AverageSpeed")]);
    }
}
