// =============================================================================
// SequentialScheduler.cs  (AbmFramework.Engine.Scheduler)
//
// Keeps the agents in the same order every tick (the order they were
// created, newborns at the end). Simple and predictable, handy for testing,
// but agents created first always get the first move.
// =============================================================================

using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

/// <summary>Lets the agents act in their existing order every tick.</summary>
public sealed class SequentialScheduler : IScheduler
{
    /// <summary>Returns the agents unchanged. The random generator isn't used.</summary>
    public IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random)
    {
        return agents;
    }
}
