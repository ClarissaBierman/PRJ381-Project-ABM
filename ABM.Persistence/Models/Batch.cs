// =============================================================================
// Batch.cs  (ABM.Persistence.Models)
//
// One Monte Carlo batch from the Compare panel (for example "SIR, 100 runs,
// base seed 7"). When two settings are compared side by side, setting A and
// setting B are two batches that share the same GroupId.
//
// The individual runs of a batch are normal Simulation rows with BatchId set.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>A saved Compare batch: its settings and the finished summary.</summary>
    public class Batch
    {
        /// <summary>Unique id of this batch.</summary>
        public Guid Id { get; set; }

        /// <summary>Shared by setting A and setting B of the same comparison.</summary>
        public Guid GroupId { get; set; }

        /// <summary>"A" or "B" (a single, non side-by-side batch is "A").</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The model's name, e.g. "SIR".</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>When the batch was saved (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>How many runs the batch has.</summary>
        public int Runs { get; set; }

        /// <summary>The seed every run's seed was made from. Reusing it repeats the whole batch.</summary>
        public int BaseSeed { get; set; }

        /// <summary>Ticks per run.</summary>
        public int TickLimit { get; set; }

        /// <summary>The full SimulationConfig as JSON (with the base settings, not each run's seed).</summary>
        public string ConfigurationJson { get; set; } = string.Empty;

        /// <summary>The slider values as the Compare panel showed them, so the panel can be filled in again.</summary>
        public string? PageSettingsJson { get; set; }

        /// <summary>The finished ComparisonSummary (table, averages and chart data) exactly as the page received it.</summary>
        public string SummaryJson { get; set; } = string.Empty;
    }

    /// <summary>One run of a batch, bundled with its grid and tick records, ready to save together.</summary>
    public class BatchRun
    {
        /// <summary>The run itself (BatchId and RunNumber should be set).</summary>
        public required Simulation Simulation { get; init; }

        /// <summary>The run's grid settings.</summary>
        public required EnvironmentRecord Environment { get; init; }

        /// <summary>Every tick's statistics.</summary>
        public required IReadOnlyList<TickRecord> Ticks { get; init; }
    }
}
