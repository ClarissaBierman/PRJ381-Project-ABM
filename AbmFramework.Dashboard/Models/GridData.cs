namespace AbmFramework.Dashboard.Models;

public class GridData
{
    public int RunId { get; set; }
    public List<AgentDTO> Agents { get; set; } = new();

    // Only patches that have something to show (food, pheromone, nest,
    // eaten grass), so big grids don't send thousands of empty cells.
    public List<PatchDTO> Patches { get; set; } = new();

    // True when every patch not listed in Patches is grass (Wolf-Sheep),
    // so the page paints the whole grid green and only the eaten patches
    // need sending.
    public bool HasGrass { get; set; }
}
