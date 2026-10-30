// =============================================================================
// PatchRecord.cs  (ABM.Persistence.Models)
//
// One row of the Patches table: the data stored on one grid cell (grass,
// food, pheromone) at the start or the end of a run. Only cells that hold
// something are saved.
// =============================================================================

namespace ABM.Persistence.Models
{
    /// <summary>The properties of one grid cell at one moment.</summary>
    public class PatchRecord
    {
        /// <summary>The grid (EnvironmentRecord.Id) this cell belongs to.</summary>
        public Guid EnvironmentId { get; set; }

        /// <summary>-1 is the starting layout, otherwise the tick the run ended on.</summary>
        public int TickNumber { get; set; }

        /// <summary>Grid column.</summary>
        public int X { get; set; }

        /// <summary>Grid row.</summary>
        public int Y { get; set; }

        /// <summary>The cell's properties as JSON, e.g. {"grass":true} or {"food":12,"pheromone":0.4}.</summary>
        public string PropertiesJson { get; set; } = "{}";
    }
}
