using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

public interface IScheduler
{
    //Returns the agents in the order they should execute for the current simulation tick.
    IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random);
}
