using AbmFramework.Config;

namespace AbmFramework.Engine;

public interface ISimulationEngine
{
    event EventHandler<TickStatistics>? TickCompleted;
    event EventHandler? SimulationCompleted;
    bool IsRunning { get; }
    bool IsPaused { get; }
    int CurrentTick { get; }
    int TickDelayMs { get; set; }

    Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    // Runs a simulation to completion with no per-tick delay (unlike
    // StartAsync, which paces itself so a live dashboard can keep up) and
    // returns the full tick-by-tick statistics history. Used for batch /
    // Monte Carlo runs where many replicates need to finish quickly.
    Task<IReadOnlyList<TickStatistics>> RunToCompletionAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    void Pause();
    void Resume();
    void Reset();
}