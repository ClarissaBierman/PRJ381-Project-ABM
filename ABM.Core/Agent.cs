using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Core
{
    public abstract class Agent
    {
        public int Id { get; }
        public (int X, int Y) Position { get; set; }

        protected Agent(int id, (int X, int Y) position)
        {
            Id = id;
            Position = position;
        }

        // environment gives agents read/write access to patches (needed by
        // models like Ant Foraging that rely on shared environment state
        // such as pheromone trails), in addition to the pre-computed
        // neighbour list the engine already resolves via the grid.
        public abstract void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment);

        // Human-readable label for the agent's current state, used by the
        // dashboard and persistence layer to display/record any agent type
        // generically. Subtypes override this to expose their own state.
        public virtual string DisplayState => GetType().Name;
    }
}
