using ABM.Core;
using AbmFramework.Engine.Scheduler;
using Xunit;

namespace AbmFramework.Engine.Tests;

public class SchedulerTests
{
    private static List<Agent> Agents(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (Agent)new SIRAgent(i, (i, 0), 0.5, 3))
            .ToList();

    [Fact]
    public void Sequential_KeepsTheExistingOrder()
    {
        var agents = Agents(10);

        var ordered = new SequentialScheduler().OrderAgents(agents, new Random(1)).ToList();

        Assert.Equal(agents, ordered);
    }

    [Fact]
    public void Random_ReturnsEveryAgentExactlyOnce()
    {
        var agents = Agents(30);

        var ordered = new RandomScheduler().OrderAgents(agents, new Random(1)).ToList();

        Assert.Equal(agents.Select(a => a.Id).Order(), ordered.Select(a => a.Id).Order());
    }

    [Fact]
    public void Random_ShufflesTheOrder()
    {
        var agents = Agents(30);

        var ordered = new RandomScheduler().OrderAgents(agents, new Random(1)).ToList();

        Assert.NotEqual(agents.Select(a => a.Id), ordered.Select(a => a.Id));
    }

    [Fact]
    public void Random_SameSeedGivesSameOrder()
    {
        var agents = Agents(30);

        var first = new RandomScheduler().OrderAgents(agents, new Random(9)).Select(a => a.Id).ToList();
        var second = new RandomScheduler().OrderAgents(agents, new Random(9)).Select(a => a.Id).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Random_DoesNotReorderTheInputList()
    {
        var agents = Agents(30);
        var originalIds = agents.Select(a => a.Id).ToList();

        _ = new RandomScheduler().OrderAgents(agents, new Random(1)).ToList();

        Assert.Equal(originalIds, agents.Select(a => a.Id));
    }
}
