namespace ABM.Persistence.Models
{
    public class EnvironmentRecord
    {
        public Guid Id { get; set; }

        public Guid SimulationId { get; set; }

        public int GridWidth { get; set; }

        public int GridHeight { get; set; }

        // "Toroidal" (wrap around edges) or "Bounded".
        public string Topology { get; set; } = string.Empty;

        public string Scheduler { get; set; } = string.Empty;
    }
}
