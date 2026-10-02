using AbmFramework.Config;
using AbmFramework.Dashboard.Hubs;
using AbmFramework.Dashboard.Models;
using AbmFramework.Engine;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard;

public class LiveSimulationService
{
    private readonly IHubContext<SimulationHub> _hubContext;
    private readonly ILogger<LiveSimulationService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SimulationEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _nextRunId = 1;

    public const int MinTickDelayMs = 10;
    public const int MaxTickDelayMs = 1000;

    public RunInfo? CurrentRun { get; private set; }
    public string Status { get; private set; } = "Stopped";
    public int TickDelayMs { get; private set; } = 50;

    public LiveSimulationService(IHubContext<SimulationHub> hubContext, ILogger<LiveSimulationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(ModelType model)
    {
        await _lock.WaitAsync();
        try
        {
            await StopCurrentAsync();

            var config = ModelCatalog.BuildConfig(model);
            var run = new RunInfo
            {
                RunId = _nextRunId++,
                Model = model.ToString(),
                DisplayName = ModelCatalog.DisplayName(model),
                Description = ModelCatalog.Description(model),
                GridWidth = config.GridWidth,
                GridHeight = config.GridHeight,
                AgentCount = config.AgentCount,
                TickLimit = config.TickLimit
            };

            var engine = new SimulationEngine { TickDelayMs = TickDelayMs };
            var cts = new CancellationTokenSource();

            engine.TickCompleted += (sender, stats) => OnTick(engine, run.RunId, stats, cts.Token);
            engine.SimulationCompleted += (sender, e) =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(engine, _engine))
                {
                    _ = SetStatusAsync("Completed");
                }
            };

            _engine = engine;
            _cts = cts;
            CurrentRun = run;

            await _hubContext.Clients.All.SendAsync("SimulationStarted", run);
            await SetStatusAsync("Running");

            _runTask = Task.Run(() => RunAsync(engine, config, cts.Token));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task PauseAsync()
    {
        if (_engine is null || !_engine.IsRunning) return;

        _engine.Pause();
        await SetStatusAsync("Paused");
    }

    public async Task ResumeAsync()
    {
        if (_engine is null || !_engine.IsPaused) return;

        _engine.Resume();
        await SetStatusAsync("Running");
    }

    public async Task SetSpeedAsync(int tickDelayMs)
    {
        TickDelayMs = Math.Clamp(tickDelayMs, MinTickDelayMs, MaxTickDelayMs);

        var engine = _engine;
        if (engine is not null)
        {
            engine.TickDelayMs = TickDelayMs;
        }

        await _hubContext.Clients.All.SendAsync("SpeedChanged", TickDelayMs);
    }

    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopCurrentAsync();
            CurrentRun = null;

            await _hubContext.Clients.All.SendAsync("SimulationReset");
            await SetStatusAsync("Stopped");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunAsync(SimulationEngine engine, SimulationConfig config, CancellationToken token)
    {
        try
        {
            await engine.StartAsync(config, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation run failed");
            await SetStatusAsync("Error");
        }
    }

    private async Task StopCurrentAsync()
    {
        if (_cts is null) return;

        _cts.Cancel();

        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _engine?.Reset();
        _cts.Dispose();

        _engine = null;
        _cts = null;
        _runTask = null;
    }

    // Runs synchronously inside the engine's tick loop, so the agent list is
    // copied before the next tick starts moving agents around.
    private void OnTick(SimulationEngine engine, int runId, TickStatistics stats, CancellationToken token)
    {
        var tick = new TickData
        {
            RunId = runId,
            Tick = stats.Tick,
            Susceptible = stats.Susceptible,
            Infected = stats.Infected,
            Recovered = stats.Recovered,
            Metrics = stats.Metrics
        };

        var grid = new GridData
        {
            RunId = runId,
            Agents = engine.GetSnapshot().Agents.Select(a => new AgentDTO
            {
                Id = a.Id,
                X = a.Position.X,
                Y = a.Position.Y,
                State = a.DisplayState
            }).ToList()
        };

        _ = BroadcastAsync(tick, grid, token);
    }

    private async Task BroadcastAsync(TickData tick, GridData grid, CancellationToken token)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveTick", tick, token);
            await _hubContext.Clients.All.SendAsync("ReceiveGrid", grid, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast tick {Tick}", tick.Tick);
        }
    }

    private async Task SetStatusAsync(string status)
    {
        Status = status;
        await _hubContext.Clients.All.SendAsync("SimulationStatus", status);
    }
}
