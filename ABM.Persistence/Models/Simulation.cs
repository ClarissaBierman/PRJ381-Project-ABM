using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Persistence.Models
{
    public class Simulation
    {
        public Guid Id { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? EndedAt { get; set; }

        public string ScenarioName { get; set; } = string.Empty;

        // Stores the configuration file used for this simulation.
        public string ConfigurationJson { get; set; } = string.Empty;
    }
}
