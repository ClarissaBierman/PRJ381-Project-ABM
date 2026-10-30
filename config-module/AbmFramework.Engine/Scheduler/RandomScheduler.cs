// =============================================================================
// RandomScheduler.cs  (AbmFramework.Engine.Scheduler)
//
// Shuffles the agents into a new random order every tick, so no agent always
// gets to go first. This is how NetLogo's "ask turtles" behaves, and it is the
// default for every model preset and scenario file in this project.
// =============================================================================

using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

/// <summary>Gives the agents a fresh random order every tick.</summary>
public sealed class RandomScheduler : IScheduler
{
    /// <summary>Returns a shuffled copy of the agent list. The original list is not changed.</summary>
    public IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random)
    {
        // Copy the list so the original order is preserved.
        var shuffled = agents.ToList();

        // Fisher-Yates shuffle: walk backwards and swap each item with a
        // random earlier (or the same) item. Every order is equally likely.
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);

            (shuffled[i], shuffled[j]) =
                (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }
}
