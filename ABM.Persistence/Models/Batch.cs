namespace ABM.Persistence.Models
{
    // One Monte Carlo batch from the Compare panel. When two settings are
    // compared side by side, setting A and setting B are two batches that
    // share the same GroupId.
    public class Batch
    {
        public Guid Id { get; set; }

        public Guid GroupId { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public int Runs { get; set; }

        public int BaseSeed { get; set; }

        public int TickLimit { get; set; }

        public string ConfigurationJson { get; set; } = string.Empty;

        // The slider values as the Compare panel showed them, so the panel can be filled in again.
        public string? PageSettingsJson { get; set; }

        // The finished summary (table, averages and chart data) exactly as the page received it.
        public string SummaryJson { get; set; } = string.Empty;
    }

    public class BatchRun
    {
        public required Simulation Simulation { get; init; }

        public required EnvironmentRecord Environment { get; init; }

        public required IReadOnlyList<TickRecord> Ticks { get; init; }
    }
}
