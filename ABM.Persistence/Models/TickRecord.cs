namespace ABM.Persistence.Models
{
    public class TickRecord
    {
        public Guid SimulationId { get; set; }

        public int TickNumber { get; set; }

        public int Susceptible { get; set; }

        public int Infected { get; set; }

        public int Recovered { get; set; }

        public int TotalPopulation { get; set; }

        // Each model's own values, e.g. "Sheep" and "Wolves" for Wolf-Sheep.
        public IReadOnlyDictionary<string, double> Metrics { get; set; } =
            new Dictionary<string, double>();
    }
}
