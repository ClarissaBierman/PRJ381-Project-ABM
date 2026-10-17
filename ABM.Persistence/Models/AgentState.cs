namespace ABM.Persistence.Models
{
    public class AgentState
    {
        public Guid SimulationId { get; set; }

        // -1 is the starting layout from Setup, before the first tick runs.
        public int TickNumber { get; set; }

        public int AgentId { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public string State { get; set; } = string.Empty;

        // Extra values for some breeds, e.g. {"energy":12.5} or {"heading":1.57,"speed":0.8}.
        public string? PropertiesJson { get; set; }
    }
}
