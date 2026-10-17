using ABM.Persistence.Models;

namespace ABM.Persistence
{
    public interface IRepository
    {
        Task SaveSimulationAsync(Simulation simulation);

        Task UpdateSimulationAsync(Guid simulationId, string status, DateTime? endedAt, int tickCount);

        // Runs still marked "Running" when the program last closed get the given status instead.
        Task<int> CloseUnfinishedRunsAsync(string status);

        Task SaveEnvironmentAsync(EnvironmentRecord environment);

        Task SaveTickRecordAsync(TickRecord record);

        Task SaveTickRecordsAsync(IReadOnlyList<TickRecord> records);

        // Adds new agents and updates LastTick for ones already saved.
        Task SaveAgentsAsync(IReadOnlyList<AgentRecord> agents);

        Task SaveAgentStatesAsync(IReadOnlyList<AgentState> states);

        Task SavePatchesAsync(IReadOnlyList<PatchRecord> patches);

        // Saves a finished Compare batch and every run in it in one go.
        Task SaveBatchAsync(Batch batch, IReadOnlyList<BatchRun> runs);

        Task<Simulation?> GetSimulationAsync(Guid simulationId);

        Task<EnvironmentRecord?> GetEnvironmentAsync(Guid simulationId);

        Task<TickRecord[]> GetTicksAsync(Guid simulationId);

        Task<AgentRecord[]> GetAgentsAsync(Guid simulationId);

        Task<AgentState[]> GetAgentStatesAsync(Guid simulationId);

        Task<PatchRecord[]> GetPatchesAsync(Guid environmentId);

        // Newest first. source and model are optional filters.
        Task<RunListItem[]> ListSimulationsAsync(string? source = null, string? model = null, int limit = 200);

        // Newest first, without the summary JSON.
        Task<Batch[]> ListBatchesAsync(int limit = 200);

        Task<Batch[]> GetBatchGroupAsync(Guid groupId);

        Task<Simulation[]> GetBatchRunsAsync(Guid batchId);

        Task<bool> DeleteSimulationAsync(Guid simulationId);

        Task<int> DeleteBatchGroupAsync(Guid groupId);
    }
}
