using AbmFramework.Config;

namespace AbmFramework.Engine;

public interface ISimulationEngine
{
    event EventHandler<TickStatistics>? TickCompleted;
    event EventHandler? SimulationCompleted;
    bool IsRunning { get; }
    bool IsPaused { get; }
    int CurrentTick { get; }

    Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default);

    void Pause();
    void Resume();
    void Reset();
}