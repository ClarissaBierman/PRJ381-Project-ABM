// =============================================================================
// GridData.cs  (AbmFramework.Dashboard.Models)
//
// Everything the page needs to draw the grid once: the agents and the
// coloured cells. Sent with the "ReceiveGrid" message.
// =============================================================================

namespace AbmFramework.Dashboard.Models;

/// <summary>A full picture of the grid at one moment.</summary>
public class GridData
{
    /// <summary>Which run this belongs to. The page ignores grids from an old run.</summary>
    public int RunId { get; set; }

    /// <summary>Every living agent.</summary>
    public List<AgentDTO> Agents { get; set; } = new();

    /// <summary>
    /// Only patches that have something to show (food, pheromone, nest,
    /// eaten grass), so big grids don't send thousands of empty cells.
    /// </summary>
    public List<PatchDTO> Patches { get; set; } = new();

    /// <summary>
    /// True when every patch not listed in Patches is grass (Wolf-Sheep),
    /// so the page paints the whole grid green and only the eaten patches
    /// need sending.
    /// </summary>
    public bool HasGrass { get; set; }
}
