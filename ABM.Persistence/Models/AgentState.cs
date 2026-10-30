// =============================================================================
// AgentState.cs  (ABM.Persistence.Models)
//
// One row of the AgentStates table: where one agent was, and what state it
// was in, at one tick. Not every tick is saved (that would be huge), only
// regular samples plus the start and the end of the run.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>An agent's position and state at one tick.</summary>
    public class AgentState
    {
        /// <summary>The run this belongs to.</summary>
        public Guid SimulationId { get; set; }

        /// <summary>The tick. -1 is the starting layout from Setup, before the first tick runs.</summary>
        public int TickNumber { get; set; }

        /// <summary>The agent's id inside the run.</summary>
        public int AgentId { get; set; }

        /// <summary>Grid column.</summary>
        public int X { get; set; }

        /// <summary>Grid row.</summary>
        public int Y { get; set; }

        /// <summary>The agent's DisplayState, e.g. "Infected" or "Group1-Happy".</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>Extra values for some breeds as JSON, e.g. {"energy":12.5} or {"heading":1.57,"speed":0.8}.</summary>
        public string? PropertiesJson { get; set; }
    }
}
