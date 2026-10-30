// =============================================================================
// RunListItem.cs  (ABM.Persistence.Models)
//
// One row of the Past runs list: the run plus its grid size, fetched in one
// query so the list doesn't need a second lookup per row.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>One row of the Past runs list.</summary>
    public class RunListItem
    {
        /// <summary>The run.</summary>
        public required Simulation Simulation { get; init; }

        /// <summary>Grid columns, for the "Grid" column of the list.</summary>
        public int GridWidth { get; init; }

        /// <summary>Grid rows.</summary>
        public int GridHeight { get; init; }
    }
}
