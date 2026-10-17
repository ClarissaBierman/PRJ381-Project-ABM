namespace ABM.Persistence.Models
{
    public class PatchRecord
    {
        public Guid EnvironmentId { get; set; }

        // -1 is the starting layout, otherwise the tick the run ended on.
        public int TickNumber { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        // e.g. {"grass":true} or {"food":12,"pheromone":0.4}
        public string PropertiesJson { get; set; } = "{}";
    }
}
