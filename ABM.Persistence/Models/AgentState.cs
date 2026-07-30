using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Persistence.Models
{
    public class AgentState
    {
        public Guid SimulationId { get; set; }

        public int TickNumber { get; set; }

        public int AgentId { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public string HealthState { get; set; } = string.Empty;
    }
}
