// =============================================================================
// Simulation.cs  (ABM.Persistence.Models)
//
// One row of the Simulations table: one run, from the dashboard, from a
// Compare batch or from the console app. Everything else (ticks, agents,
// environment) points back to a run through its Id.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>One simulation run.</summary>
    public class Simulation
    {
        /// <summary>Unique id of the run.</summary>
        public Guid Id { get; set; }

        /// <summary>The model's name, e.g. "WolfSheep".</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The scenario name from the config.</summary>
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>Where the run came from: "Live" (dashboard), "Compare" (one run of a batch) or "Console".</summary>
        public string Source { get; set; } = "Live";

        /// <summary>"Running", "Paused", "Completed" or "Stopped" (reset, or the app closed mid-run).</summary>
        public string Status { get; set; } = "Running";

        /// <summary>When the run started (UTC).</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>When it ended (UTC). Null while still running.</summary>
        public DateTime? EndedAt { get; set; }

        /// <summary>How many ticks were completed.</summary>
        public int TickCount { get; set; }

        /// <summary>The random seed. Together with ConfigurationJson this is enough to replay the run exactly.</summary>
        public int? Seed { get; set; }

        /// <summary>The full SimulationConfig used for this run, as JSON.</summary>
        public string ConfigurationJson { get; set; } = string.Empty;

        /// <summary>The Compare batch this run belongs to. Null for a normal run.</summary>
        public Guid? BatchId { get; set; }

        /// <summary>Position inside the batch, starting at 1. Null for a normal run.</summary>
        public int? RunNumber { get; set; }
    }
}
