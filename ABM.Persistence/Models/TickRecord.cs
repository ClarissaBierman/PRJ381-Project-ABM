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
    }
}
