// =============================================================================
// TickRecord.cs  (ABM.Persistence.Models)
//
// One row of the Ticks table: the statistics of one tick. The database
// version of the engine's TickStatistics (same fields).
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>The statistics of one tick of one run.</summary>
    public class TickRecord
    {
        /// <summary>The run this tick belongs to.</summary>
        public Guid SimulationId { get; set; }

        /// <summary>The tick number, starting at 0.</summary>
        public int TickNumber { get; set; }

        /// <summary>SIR only, 0 for other models.</summary>
        public int Susceptible { get; set; }

        /// <summary>SIR only, 0 for other models.</summary>
        public int Infected { get; set; }

        /// <summary>SIR only, 0 for other models.</summary>
        public int Recovered { get; set; }

        /// <summary>Number of living agents.</summary>
        public int TotalPopulation { get; set; }

        /// <summary>Each model's own values, e.g. "Sheep" and "Wolves" for Wolf-Sheep. Saved as JSON.</summary>
        public IReadOnlyDictionary<string, double> Metrics { get; set; } =
            new Dictionary<string, double>();
    }
}
