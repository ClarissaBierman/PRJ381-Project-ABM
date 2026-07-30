using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

public sealed class SequentialScheduler : IScheduler
{
    //Returns the agents in their existing order.
    public IEnumerable<SIRAgent> OrderAgents(
        IReadOnlyList<SIRAgent> agents,
        Random random)
    {
        return agents;
    }
}