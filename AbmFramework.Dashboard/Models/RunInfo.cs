// =============================================================================
// RunInfo.cs  (AbmFramework.Dashboard.Models)
//
// Describes the run that just started. Sent with the "SimulationStarted"
// message so the page can set up its chart, title and grid size.
// =============================================================================

namespace AbmFramework.Dashboard.Models;

/// <summary>Information about a run that has just started.</summary>
public class RunInfo
{
    /// <summary>
    /// A number that goes up by one for every new run. Every tick and grid
    /// message carries it, so the page can ignore late messages from an old run.
    /// </summary>
    public int RunId { get; set; }

    /// <summary>The ModelType name, e.g. "SIR".</summary>
    public string Model { get; set; } = "";

    /// <summary>The friendly model name.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>The model's description.</summary>
    public string Description { get; set; } = "";

    /// <summary>Grid columns.</summary>
    public int GridWidth { get; set; }

    /// <summary>Grid rows.</summary>
    public int GridHeight { get; set; }

    /// <summary>Number of agents at the start.</summary>
    public int AgentCount { get; set; }

    /// <summary>How many ticks the run lasts.</summary>
    public int TickLimit { get; set; }

    /// <summary>The run's id in the database (for Past runs), or null if saving is off.</summary>
    public string? HistoryId { get; set; }
}
