using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Engine.Tests;

// Model-independent behaviour of the engine: the tick loop, events,
// determinism, and the Start/Pause/Resume/Reset lifecycle.
public class SimulationEngineTests
{
    [Theory]
    [MemberData(nameof(TestConfigs.AllModels), MemberType = typeof(TestConfigs))]
    public async Task RunToCompletion_RunsExactlyTickLimitTicks(ModelType model)
    {
        var engine = new SimulationEngine();

        var history = await engine.RunToCompletionAsync(TestConfigs.For(model, tickLimit: 25));

        Assert.Equal(Enumerable.Range(0, 25), history.Select(s => s.Tick));
        Assert.Equal(25, engine.CurrentTick);
        Assert.Equal(SimulationState.Completed, engine.State);
        Assert.False(engine.IsRunning);
    }

    [Theory]
    [MemberData(nameof(TestConfigs.AllModels), MemberType = typeof(TestConfigs))]
    public async Task SameSeed_ProducesIdenticalRuns(ModelType model)
    {
        var first = await new SimulationEngine().RunToCompletionAsync(TestConfigs.For(model, seed: 7));
        var second = await new SimulationEngine().RunToCompletionAsync(TestConfigs.For(model, seed: 7));

        Assert.Equal(TestConfigs.Signatures(first), TestConfigs.Signatures(second));
    }

    [Fact]
    public async Task DifferentSeeds_ProduceDifferentRuns()
    {
        var first = await new SimulationEngine().RunToCompletionAsync(TestConfigs.For(ModelType.Boids, seed: 1));
        var second = await new SimulationEngine().RunToCompletionAsync(TestConfigs.For(ModelType.Boids, seed: 2));

        Assert.NotEqual(TestConfigs.Signatures(first), TestConfigs.Signatures(second));
    }

    [Fact]
    public async Task SameEngine_RunTwice_StartsFreshEachTime()
    {
        var engine = new SimulationEngine();
        var config = TestConfigs.For(ModelType.WolfSheep, seed: 5);

        var first = await engine.RunToCompletionAsync(config);
        var second = await engine.RunToCompletionAsync(config);

        Assert.Equal(TestConfigs.Signatures(first), TestConfigs.Signatures(second));
    }

    [Fact]
    public async Task RunToCompletion_RaisesTickCompletedEachTickAndSimulationCompletedOnce()
    {
        var engine = new SimulationEngine();
        var ticksSeen = new List<int>();
        int completedCount = 0;
        engine.TickCompleted += (_, stats) => ticksSeen.Add(stats.Tick);
        engine.SimulationCompleted += (_, _) => completedCount++;

        await engine.RunToCompletionAsync(TestConfigs.For(ModelType.SIR, tickLimit: 12));

        Assert.Equal(Enumerable.Range(0, 12), ticksSeen);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public async Task RunToCompletion_WhenCancelled_StopsAndReturnsTicksSoFar()
    {
        var engine = new SimulationEngine();
        using var cts = new CancellationTokenSource();
        engine.TickCompleted += (_, stats) => { if (stats.Tick == 3) cts.Cancel(); };

        var history = await engine.RunToCompletionAsync(TestConfigs.For(ModelType.SIR, tickLimit: 100), cts.Token);

        Assert.Equal(4, history.Count);
    }

    [Fact]
    public async Task StartAsync_RunsToTickLimitAndRaisesEvents()
    {
        var engine = new SimulationEngine { TickDelayMs = 0 };
        int ticks = 0;
        int completedCount = 0;
        engine.TickCompleted += (_, _) => ticks++;
        engine.SimulationCompleted += (_, _) => completedCount++;

        await engine.StartAsync(TestConfigs.For(ModelType.Schelling, tickLimit: 10));

        Assert.Equal(10, ticks);
        Assert.Equal(1, completedCount);
        Assert.Equal(SimulationState.Completed, engine.State);
    }

    [Fact]
    public async Task StartAsync_WhenCancelled_StopsBeforeTickLimit()
    {
        var engine = new SimulationEngine { TickDelayMs = 0 };
        using var cts = new CancellationTokenSource();
        engine.TickCompleted += (_, stats) => { if (stats.Tick == 3) cts.Cancel(); };

        try
        {
            await engine.StartAsync(TestConfigs.For(ModelType.SIR, tickLimit: 100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // StartAsync may surface the cancellation from its tick delay.
        }

        Assert.Equal(4, engine.CurrentTick);
    }

    [Fact]
    public async Task PauseAndResume_HoldTheTickCountThenFinish()
    {
        var engine = new SimulationEngine { TickDelayMs = 0 };
        var paused = new TaskCompletionSource();
        engine.TickCompleted += (_, stats) =>
        {
            if (stats.Tick == 5)
            {
                engine.Pause();
                paused.TrySetResult();
            }
        };

        var run = engine.StartAsync(TestConfigs.For(ModelType.SIR, tickLimit: 20));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(250);
        int tickWhilePaused = engine.CurrentTick;
        await Task.Delay(250);

        Assert.True(engine.IsPaused);
        Assert.Equal(6, tickWhilePaused);
        Assert.Equal(tickWhilePaused, engine.CurrentTick);

        engine.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(20, engine.CurrentTick);
        Assert.Equal(SimulationState.Completed, engine.State);
    }

    [Fact]
    public async Task Starting_WhileAlreadyRunning_Throws()
    {
        // A long tick delay keeps the first run sitting between ticks.
        var engine = new SimulationEngine { TickDelayMs = 10_000 };
        var firstTick = new TaskCompletionSource();
        engine.TickCompleted += (_, _) => firstTick.TrySetResult();
        using var cts = new CancellationTokenSource();
        var run = engine.StartAsync(TestConfigs.For(ModelType.SIR, tickLimit: 50), cts.Token);
        await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(engine.IsRunning);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.RunToCompletionAsync(TestConfigs.For(ModelType.SIR)));

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Reset_ClearsTheSimulation()
    {
        var engine = new SimulationEngine();
        await engine.RunToCompletionAsync(TestConfigs.For(ModelType.SIR));

        engine.Reset();

        Assert.Equal(SimulationState.Stopped, engine.State);
        Assert.Equal(0, engine.CurrentTick);
        Assert.Throws<InvalidOperationException>(() => engine.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_BeforeAnyRun_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new SimulationEngine().GetSnapshot());
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(250, 250)]
    [InlineData(50_000, 10_000)]
    public void TickDelayMs_IsClampedToZeroToTenSeconds(int requested, int expected)
    {
        var engine = new SimulationEngine { TickDelayMs = requested };

        Assert.Equal(expected, engine.TickDelayMs);
    }
}
