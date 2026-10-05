using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Engine.Tests;

public class SetupAndStepTests
{
    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var waited = 0;
        while (!condition())
        {
            if (waited >= timeoutMs) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
            waited += 10;
        }
    }

    [Fact]
    public async Task StartPaused_CreatesAgentsButRunsNoTicks()
    {
        var engine = new SimulationEngine { StartPaused = true, TickDelayMs = 1 };
        var initialised = false;
        var ticks = 0;
        engine.SimulationInitialised += (_, _) => initialised = true;
        engine.TickCompleted += (_, _) => ticks++;

        using var cts = new CancellationTokenSource();
        var run = engine.StartAsync(TestConfigs.For(ModelType.SIR), cts.Token);

        await WaitFor(() => initialised);
        await Task.Delay(150);

        Assert.True(engine.IsPaused);
        Assert.Equal(0, ticks);
        Assert.Equal(0, engine.CurrentTick);
        Assert.NotEmpty(engine.GetSnapshot().Agents);

        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => run);
    }

    [Fact]
    public async Task Step_RunsExactlyOneTickAndStaysPaused()
    {
        var engine = new SimulationEngine { StartPaused = true, TickDelayMs = 1 };
        var ticks = 0;
        engine.TickCompleted += (_, _) => ticks++;

        using var cts = new CancellationTokenSource();
        var run = engine.StartAsync(TestConfigs.For(ModelType.SIR), cts.Token);
        await WaitFor(() => engine.IsPaused);

        engine.Step();
        await WaitFor(() => ticks == 1);
        await Task.Delay(150);

        Assert.Equal(1, ticks);
        Assert.Equal(1, engine.CurrentTick);
        Assert.True(engine.IsPaused);

        engine.Step();
        await WaitFor(() => ticks == 2);
        Assert.Equal(2, engine.CurrentTick);

        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => run);
    }

    [Fact]
    public async Task Resume_AfterSetup_RunsToTheEnd()
    {
        var engine = new SimulationEngine { StartPaused = true, TickDelayMs = 1 };
        var ticks = 0;
        engine.TickCompleted += (_, _) => ticks++;

        var run = engine.StartAsync(TestConfigs.For(ModelType.Schelling, tickLimit: 20));
        await WaitFor(() => engine.IsPaused);

        engine.Resume();
        await run;

        Assert.Equal(20, ticks);
        Assert.Equal(SimulationState.Completed, engine.State);
    }

    [Fact]
    public async Task Step_IsIgnoredWhileRunning()
    {
        var engine = new SimulationEngine { TickDelayMs = 1 };
        var ticks = 0;
        engine.TickCompleted += (_, _) =>
        {
            ticks++;
            engine.Step();
        };

        await engine.StartAsync(TestConfigs.For(ModelType.Boids, tickLimit: 15));

        Assert.Equal(15, ticks);
    }

    [Fact]
    public async Task WithoutStartPaused_RunsStraightAway()
    {
        var engine = new SimulationEngine { TickDelayMs = 1 };
        var initialised = false;
        engine.SimulationInitialised += (_, _) => initialised = true;

        var ticks = await CountTicks(engine, TestConfigs.For(ModelType.AntForaging, tickLimit: 10));

        Assert.True(initialised);
        Assert.Equal(10, ticks);
    }

    private static async Task<int> CountTicks(SimulationEngine engine, SimulationConfig config)
    {
        var ticks = 0;
        engine.TickCompleted += (_, _) => ticks++;
        await engine.StartAsync(config);
        return ticks;
    }
}
