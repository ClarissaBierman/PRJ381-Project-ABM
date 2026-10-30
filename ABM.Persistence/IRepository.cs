// =============================================================================
// IRepository.cs  (ABM.Persistence)
//
// Everything the app can save to and read from the database. The dashboard
// and the console app only talk to the database through this interface,
// never with SQL directly. SimulationRepository is the SQLite implementation.
//
// All methods are async because database work is slow compared to the
// simulation. In the dashboard they run on a background writer (HistoryWriter)
// so the simulation never waits for the disk.
// =============================================================================

using ABM.Persistence.Models;

namespace ABM.Persistence
{
    /// <summary>Saves and loads simulation runs, their ticks and agents, and Compare batches.</summary>
    public interface IRepository
    {
        // ---- Saving ----------------------------------------------------------

        /// <summary>Adds a new run. Call this once, when the run starts.</summary>
        Task SaveSimulationAsync(Simulation simulation);

        /// <summary>Updates a run's status, end time and tick count (for example when it is paused or finished).</summary>
        Task UpdateSimulationAsync(Guid simulationId, string status, DateTime? endedAt, int tickCount);

        /// <summary>
        /// Runs still marked "Running" when the program last closed get the given
        /// status instead. Called once at start-up.
        /// </summary>
        /// <returns>How many runs were changed.</returns>
        Task<int> CloseUnfinishedRunsAsync(string status);

        /// <summary>Saves a run's grid settings.</summary>
        Task SaveEnvironmentAsync(EnvironmentRecord environment);

        /// <summary>Saves one tick's statistics.</summary>
        Task SaveTickRecordAsync(TickRecord record);

        /// <summary>Saves many ticks in one transaction (much faster than one at a time).</summary>
        Task SaveTickRecordsAsync(IReadOnlyList<TickRecord> records);

        /// <summary>Adds new agents and updates LastTick for ones already saved.</summary>
        Task SaveAgentsAsync(IReadOnlyList<AgentRecord> agents);

        /// <summary>Saves agent positions and states. A row for the same run, tick and agent is replaced.</summary>
        Task SaveAgentStatesAsync(IReadOnlyList<AgentState> states);

        /// <summary>Saves cell data. A row for the same grid, tick and cell is replaced.</summary>
        Task SavePatchesAsync(IReadOnlyList<PatchRecord> patches);

        /// <summary>Saves a finished Compare batch and every run in it in one go (all or nothing).</summary>
        Task SaveBatchAsync(Batch batch, IReadOnlyList<BatchRun> runs);

        // ---- Reading ---------------------------------------------------------

        /// <summary>Gets one run, or null if it doesn't exist.</summary>
        Task<Simulation?> GetSimulationAsync(Guid simulationId);

        /// <summary>Gets a run's grid settings, or null.</summary>
        Task<EnvironmentRecord?> GetEnvironmentAsync(Guid simulationId);

        /// <summary>Gets every saved tick of a run, in tick order.</summary>
        Task<TickRecord[]> GetTicksAsync(Guid simulationId);

        /// <summary>Gets every agent that existed in a run, by id.</summary>
        Task<AgentRecord[]> GetAgentsAsync(Guid simulationId);

        /// <summary>Gets every saved agent position and state of a run, by tick then agent.</summary>
        Task<AgentState[]> GetAgentStatesAsync(Guid simulationId);

        /// <summary>Gets the saved cell data of a grid (pass EnvironmentRecord.Id, not the run id).</summary>
        Task<PatchRecord[]> GetPatchesAsync(Guid environmentId);

        /// <summary>The Past runs list, newest first. source ("Live", "Compare", "Console") and model are optional filters.</summary>
        Task<RunListItem[]> ListSimulationsAsync(string? source = null, string? model = null, int limit = 200);

        /// <summary>All Compare batches, newest first, without the (large) summary JSON.</summary>
        Task<Batch[]> ListBatchesAsync(int limit = 200);

        /// <summary>The batches of one comparison (setting A and B), with their summaries.</summary>
        Task<Batch[]> GetBatchGroupAsync(Guid groupId);

        /// <summary>The individual runs of one batch, in run order.</summary>
        Task<Simulation[]> GetBatchRunsAsync(Guid batchId);

        // ---- Deleting --------------------------------------------------------

        /// <summary>Deletes a run and everything that belongs to it.</summary>
        /// <returns>False if the run didn't exist.</returns>
        Task<bool> DeleteSimulationAsync(Guid simulationId);

        /// <summary>Deletes a whole comparison (both batches and all their runs).</summary>
        /// <returns>How many batches were deleted.</returns>
        Task<int> DeleteBatchGroupAsync(Guid groupId);
    }
}
