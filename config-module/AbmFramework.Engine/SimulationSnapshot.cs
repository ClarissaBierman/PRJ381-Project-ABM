// =============================================================================
// SimulationSnapshot.cs  (AbmFramework.Engine)
//
// A read-only "photo" of a run at one moment, returned by
// SimulationEngine.GetSnapshot(). The dashboard turns this into the grid
// picture it sends to the browser.
//
// Careful: Grid and Agents are the engine's live objects, not copies. Read
// them right away (between ticks) and don't change them.
// =============================================================================

using ABM.Core;
using AbmFramework.Config;

namespace AbmFramework.Engine;

/// <summary>The state of a run at one moment: settings, grid, agents and latest statistics.</summary>
public sealed class SimulationSnapshot
{
    /// <summary>Configuration used to initialise the simulation.</summary>
    public required SimulationConfig Configuration { get; init; }

    /// <summary>The current environment grid (the live object, not a copy).</summary>
    public required Grid2D Grid { get; init; }

    /// <summary>All agents currently in the simulation (a read-only view of the live list).</summary>
    public required IReadOnlyList<Agent> Agents { get; init; }

    /// <summary>The latest statistics.</summary>
    public required TickStatistics Statistics { get; init; }

    /// <summary>Running, Paused, Stopped or Completed.</summary>
    public SimulationState State { get; init; }

    /// <summary>The tick these statistics belong to.</summary>
    public int Tick => Statistics.Tick;
}
