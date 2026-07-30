using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ABM.Persistence.Models;

namespace ABM.Persistence
{
    public interface IRepository
    {
        /// Creates a new simulation record.
        Task SaveSimulationAsync(Simulation simulation);

        /// Saves aggregate S/I/R values for a completed tick.
        Task SaveTickRecordAsync(TickRecord record);

        /// Saves the state of every agent for one simulation tick.
        Task SaveAgentStatesAsync(AgentState[] states);

        /// Returns all recorded ticks for a simulation.
        Task<TickRecord[]> GetTicksAsync(Guid simulationId);

        /// Returns a simulation by its ID.
        Task<Simulation?> GetSimulationAsync(Guid simulationId);
    }
}
