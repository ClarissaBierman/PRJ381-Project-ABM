using AbmFramework.Config;

namespace AbmFramework.Engine;

public interface ISimulationEngine
{
    //Raised after each simulation tick has completed.
    event EventHandler<TickStatistics>? TickCompleted;

    //Raised when the simulation finishes normally.
    event EventHandler? SimulationCompleted;

    //True while the simulation is actively executing.
    bool IsRunning { get; }

    // True when the simulation has been paused.
    bool IsPaused { get; }

    // Current simulation tick.
    int CurrentTick { get; }

    // Starts a new simulation.
    Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    // Temporarily pauses execution.
    void Pause();

    // Continues a paused simulation.
    void Resume();

    // Stops the current simulation and resets all state.
    void Reset();
}