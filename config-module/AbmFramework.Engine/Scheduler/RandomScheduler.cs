using ABM.Core;

namespace AbmFramework.Engine.Scheduler;

public sealed class RandomScheduler : IScheduler
{
    //Returns the agents in a randomly shuffled order.
    public IEnumerable<Agent> OrderAgents(
        IReadOnlyList<Agent> agents,
        Random random)
    {
        //Copy the list so the original order is preserved.
        var shuffled = agents.ToList();

        //Fisher-Yates shuffle.
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);

            (shuffled[i], shuffled[j]) =
                (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }
}
