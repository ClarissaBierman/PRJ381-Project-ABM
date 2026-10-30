// =============================================================================
// EnvironmentRecord.cs  (ABM.Persistence.Models)
//
// One row of the Environments table: the grid settings of a run. Kept in its
// own table (instead of on Simulation) so patches can belong to it.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>The grid settings of one run.</summary>
    public class EnvironmentRecord
    {
        /// <summary>Unique id. Patches point to this.</summary>
        public Guid Id { get; set; }

        /// <summary>The run this grid belongs to.</summary>
        public Guid SimulationId { get; set; }

        /// <summary>Number of columns.</summary>
        public int GridWidth { get; set; }

        /// <summary>Number of rows.</summary>
        public int GridHeight { get; set; }

        /// <summary>"Toroidal" (edges wrap around) or "Bounded" (edges are walls).</summary>
        public string Topology { get; set; } = string.Empty;

        /// <summary>"Random" or "Sequential".</summary>
        public string Scheduler { get; set; } = string.Empty;
    }
}
