// =============================================================================
// Agent.cs  (ABM.Core)
//
// The base class every agent in every model inherits from. An agent is one
// small "actor" in the simulation: a person in SIR, a bird in Boids, an ant,
// a sheep, a wolf. It has an id, a position on the grid and its own rules.
//
// The engine (SimulationEngine in AbmFramework.Engine) never needs to know
// which model it is running. It only ever calls Step() on each Agent, and each
// model's subclass decides what that step means.
//
// To add a new model you create a new subclass of Agent and override Step().
// See docs/ADDING-A-MODEL.md for the full walkthrough.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// Base class for every agent in the framework. Subclasses override
    /// <see cref="Step"/> to define what the agent does on its turn.
    /// </summary>
    public abstract class Agent
    {
        /// <summary>Unique id of this agent within one run. Never reused, even after the agent dies.</summary>
        public int Id { get; }

        /// <summary>
        /// The grid cell the agent is standing on. Agents may change this inside
        /// <see cref="Step"/>. The engine notices the change afterwards and moves
        /// the agent to its new cell in the grid's lookup table.
        /// </summary>
        public (int X, int Y) Position { get; set; }

        /// <summary>
        /// False once <see cref="Die"/> has been called. Dead agents are skipped
        /// and removed by the engine at its next opportunity.
        /// </summary>
        public bool IsAlive { get; private set; } = true;

        // Babies asked for during this tick. They are stored as "factories"
        // (functions that build the new agent once it has been given an id)
        // instead of real agents, so the engine's agent list never changes
        // while it is still looping over it. See CreatePendingOffspring.
        private readonly List<Func<int, Agent>> pendingOffspring = new();

        /// <summary>Creates an agent with its id and starting cell.</summary>
        /// <param name="id">Unique id, handed out by the engine.</param>
        /// <param name="position">The starting grid cell.</param>
        protected Agent(int id, (int X, int Y) position)
        {
            Id = id;
            Position = position;
        }

        /// <summary>
        /// The agent's turn. Called once per tick by the engine, in the order the
        /// scheduler picks.
        /// </summary>
        /// <param name="neighbours">
        /// The living agents in the surrounding cells, already worked out by the
        /// engine from the grid. Agents only "see" what is around them, which is
        /// what makes the big patterns emerge.
        /// </param>
        /// <param name="rng">
        /// The run's random number generator. Always use this one (never
        /// <c>new Random()</c>) so that the same seed always gives the same run.
        /// </param>
        /// <param name="environment">
        /// Read and write access to the grid's patches (cells), for models that
        /// keep state in the environment, like grass for sheep or pheromone for ants.
        /// </param>
        public abstract void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment);

        /// <summary>
        /// A short text label for the agent's current state, for example
        /// "Infected" or "Group1-Happy". The dashboard uses it to pick the colour
        /// and icon, and the database stores it, so both work for any agent type
        /// without knowing about it. Subclasses override this. The default is the
        /// class name.
        /// </summary>
        public virtual string DisplayState => GetType().Name;

        /// <summary>
        /// Asks for a new agent to be born at the end of this tick. Used by
        /// models with reproduction, like Wolf-Sheep.
        /// </summary>
        /// <param name="createOffspring">
        /// A function that receives the new agent's id and returns the new agent.
        /// </param>
        protected void QueueOffspring(Func<int, Agent> createOffspring)
        {
            pendingOffspring.Add(createOffspring);
        }

        /// <summary>Marks the agent as dead. The engine removes it from the grid and the agent list.</summary>
        public void Die() => IsAlive = false;

        /// <summary>
        /// Builds every baby queued with <see cref="QueueOffspring"/> during this
        /// tick and clears the queue. Called by the engine after all agents have
        /// had their turn.
        /// </summary>
        /// <param name="nextId">Hands out the next free agent id.</param>
        /// <returns>The newborn agents, ready to be added to the simulation.</returns>
        public IReadOnlyList<Agent> CreatePendingOffspring(Func<int> nextId)
        {
            var offspring = pendingOffspring.Select(factory => factory(nextId())).ToList();
            pendingOffspring.Clear();
            return offspring;
        }
    }
}
