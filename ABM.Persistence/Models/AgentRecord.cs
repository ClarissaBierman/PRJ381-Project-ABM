// =============================================================================
// AgentRecord.cs  (ABM.Persistence.Models)
//
// One row of the Agents table: one agent that existed during a run, and the
// first and last tick it was alive. Lets you see when sheep were born or died.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>An agent that existed during a run, with its lifespan in ticks.</summary>
    public class AgentRecord
    {
        /// <summary>The run this agent belongs to.</summary>
        public Guid SimulationId { get; set; }

        /// <summary>The agent's id inside the run (Agent.Id).</summary>
        public int AgentId { get; set; }

        /// <summary>The kind of agent, e.g. "Sheep", "Wolf", "Ant", "Boid".</summary>
        public string Breed { get; set; } = string.Empty;

        /// <summary>The first tick the agent was seen. -1 means the agent was there from the start (Setup).</summary>
        public int FirstTick { get; set; }

        /// <summary>The last tick the agent was seen alive.</summary>
        public int LastTick { get; set; }
    }
}
