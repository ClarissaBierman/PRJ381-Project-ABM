// =============================================================================
// SimulationState.cs  (AbmFramework.Engine)
//
// The four states an engine can be in. Typical flow:
//   Stopped -> Running <-> Paused -> Completed
// =============================================================================

namespace AbmFramework.Engine;

/// <summary>Where a simulation is in its life.</summary>
public enum SimulationState
{
    /// <summary>The engine has not started or has been reset.</summary>
    Stopped,

    /// <summary>The simulation is currently executing ticks.</summary>
    Running,

    /// <summary>Execution is temporarily suspended between ticks.</summary>
    Paused,

    /// <summary>The run reached its tick limit or was cancelled.</summary>
    Completed
}