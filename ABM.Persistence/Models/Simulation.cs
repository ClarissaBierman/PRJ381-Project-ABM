namespace ABM.Persistence.Models
{
    public class Simulation
    {
        public Guid Id { get; set; }

        public string Model { get; set; } = string.Empty;

        public string ScenarioName { get; set; } = string.Empty;

        // Where the run came from: "Live" (dashboard), "Compare" (one run of a batch) or "Console".
        public string Source { get; set; } = "Live";

        public string Status { get; set; } = "Running";

        public DateTime StartedAt { get; set; }

        public DateTime? EndedAt { get; set; }

        public int TickCount { get; set; }

        public int? Seed { get; set; }

        // The full SimulationConfig used for this run.
        public string ConfigurationJson { get; set; } = string.Empty;

        public Guid? BatchId { get; set; }

        public int? RunNumber { get; set; }
    }
}
