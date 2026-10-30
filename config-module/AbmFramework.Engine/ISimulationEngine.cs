// =============================================================================
// ISimulationEngine.cs  (AbmFramework.Engine)
//
// The public contract of a simulation engine. SimulationEngine is the only
// implementation. Code that only needs to start, watch and control a run
// can depend on this interface instead of the concrete class.
// =============================================================================

using AbmFramework.Config;

namespace AbmFramework.Engine;

/// <summary>
/// Starts, runs and controls one simulation. See <see cref="SimulationEngine"/>
/// for the full explanation of each member.
/// </summary>
public interface ISimulationEngine
{
    /// <summary>Raised after every tick with that tick's statistics.</summary>
    event EventHandler<TickStatistics>? TickCompleted;

    /// <summary>Raised once when the run ends.</summary>
    event EventHandler? SimulationCompleted;

    /// <summary>True while ticks are running.</summary>
    bool IsRunning { get; }

    /// <summary>True while paused.</summary>
    bool IsPaused { get; }

    /// <summary>Number of ticks finished so far.</summary>
    int CurrentTick { get; }

    /// <summary>Delay between ticks in live mode, in milliseconds.</summary>
    int TickDelayMs { get; set; }

    /// <summary>Live mode: runs at <see cref="TickDelayMs"/> pace and supports pausing.</summary>
    Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch mode: runs a simulation to completion with no per-tick delay
    /// (unlike StartAsync, which paces itself so a live dashboard can keep up)
    /// and returns the full tick-by-tick statistics history. Used for
    /// Monte Carlo runs where many runs need to finish quickly.
    /// </summary>
    Task<IReadOnlyList<TickStatistics>> RunToCompletionAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Pauses a running simulation.</summary>
    void Pause();

    /// <summary>Resumes a paused simulation.</summary>
    void Resume();

    /// <summary>Clears all run state so the engine can be reused.</summary>
    void Reset();
}