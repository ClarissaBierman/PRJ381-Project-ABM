namespace ABM.Persistence.Models
{
    // One row of the Past runs list.
    public class RunListItem
    {
        public required Simulation Simulation { get; init; }

        public int GridWidth { get; init; }

        public int GridHeight { get; init; }
    }
}
