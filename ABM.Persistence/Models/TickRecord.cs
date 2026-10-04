using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Persistence.Models
{
    public class TickRecord
    {
        public Guid SimulationId { get; set; }

        public int TickNumber { get; set; }

        public int Susceptible { get; set; }

        public int Infected { get; set; }

        public int Recovered { get; set; }

        // Per-model metrics for non-SIR models (e.g. "HappyAgents" for
        // Schelling). Used by CsvExporter; not stored in the TickRecords
        // table yet, so records read back from the database have none.
        public IReadOnlyDictionary<string, double> Metrics { get; set; } =
            new Dictionary<string, double>();
    }
}
