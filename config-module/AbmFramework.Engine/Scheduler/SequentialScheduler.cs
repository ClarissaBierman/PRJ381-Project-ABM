using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

public sealed class SequentialScheduler : IScheduler
{
    //Returns the agents in their existing order.
    public IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random)
    {
        return agents;
    }
}
