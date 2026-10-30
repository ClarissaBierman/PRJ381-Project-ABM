// =============================================================================
// IScheduler.cs  (AbmFramework.Engine.Scheduler)
//
// A scheduler decides the ORDER in which agents take their turn each tick.
// Order matters: if a wolf moves before a sheep, the sheep might get eaten
// before it can run. Picked with SchedulerKind in SimulationConfig.
//
// To add a new ordering (for example "oldest first"), implement this
// interface and add it to the switch in SimulationEngine.InitialiseSimulation.
// =============================================================================

using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

/// <summary>Decides the order agents act in during one tick.</summary>
public interface IScheduler
{
    /// <summary>Returns the agents in the order they should act this tick.</summary>
    /// <param name="agents">All agents in the simulation. Don't change this list.</param>
    /// <param name="random">The run's random generator, so a random order is still repeatable.</param>
    IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random);
}
