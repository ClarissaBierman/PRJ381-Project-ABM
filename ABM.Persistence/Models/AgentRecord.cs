namespace ABM.Persistence.Models
{
    public class AgentRecord
    {
        public Guid SimulationId { get; set; }

        public int AgentId { get; set; }

        // The kind of agent, e.g. "Sheep", "Wolf", "Ant", "Boid".
        public string Breed { get; set; } = string.Empty;

        // -1 means the agent was there from the start.
        public int FirstTick { get; set; }

        public int LastTick { get; set; }
    }
}
