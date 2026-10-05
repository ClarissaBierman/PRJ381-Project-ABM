using ABM.Core;
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

    // The current run together with every tick it has produced so far, kept
    // in one object so the CSV download can never pair one run's info with
    // another run's ticks. Ticks are appended on the engine's thread and read
    // by download requests, so access to the list is locked on the list.
    private sealed record ActiveRun(RunInfo Info, List<TickStatistics> Ticks);
    private ActiveRun? _activeRun;

    public const int MinTickDelayMs = 10;
    public const int MaxTickDelayMs = 1000;

    public RunInfo? CurrentRun => _activeRun?.Info;
    public string Status { get; private set; } = "Stopped";
    public int TickDelayMs { get; private set; } = 50;

    public LiveSimulationService(IHubContext<SimulationHub> hubContext, ILogger<LiveSimulationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(SimulationConfig config)
    {
        ConfigLoader.Validate(config);
        await _lock.WaitAsync();
        try
        {
            await StopCurrentAsync();

            var model = config.Model;
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

            var activeRun = new ActiveRun(run, new List<TickStatistics>());
            var engine = new SimulationEngine { TickDelayMs = TickDelayMs };
            var cts = new CancellationTokenSource();

            engine.TickCompleted += (sender, stats) => OnTick(engine, activeRun, stats, cts.Token);
            engine.SimulationCompleted += (sender, e) =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(engine, _engine))
                {
                    _ = SetStatusAsync("Completed");
                }
            };

            _engine = engine;
            _cts = cts;
            _activeRun = activeRun;

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
            _activeRun = null;

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
    private void OnTick(SimulationEngine engine, ActiveRun activeRun, TickStatistics stats, CancellationToken token)
    {
        lock (activeRun.Ticks)
        {
            activeRun.Ticks.Add(stats);
        }

        var runId = activeRun.Info.RunId;
        var tick = new TickData
        {
            RunId = runId,
            Tick = stats.Tick,
            Susceptible = stats.Susceptible,
            Infected = stats.Infected,
            Recovered = stats.Recovered,
            Metrics = stats.Metrics
        };

        var grid = BuildGridData(engine.GetSnapshot(), runId);

        _ = BroadcastAsync(tick, grid, token);
    }

    private static GridData BuildGridData(SimulationSnapshot snapshot, int runId)
    {
        var grid = new GridData
        {
            RunId = runId,
            HasGrass = snapshot.Configuration.Model == ModelType.WolfSheep,
            Agents = snapshot.Agents.Select(a => new AgentDTO
            {
                Id = a.Id,
                X = a.Position.X,
                Y = a.Position.Y,
                State = a.DisplayState,
                Energy = a switch
                {
                    SheepAgent sheep => Math.Round(sheep.Energy, 1),
                    WolfAgent wolf => Math.Round(wolf.Energy, 1),
                    _ => null
                }
            }).ToList()
        };

        foreach (var patch in snapshot.Grid.AllPatches())
        {
            var dto = ToPatchDTO(patch);
            if (dto is not null)
            {
                grid.Patches.Add(dto);
            }
        }

        return grid;
    }

    // Returns null for a patch with nothing worth colouring.
    private static PatchDTO? ToPatchDTO(Patch patch)
    {
        var food = patch.GetProperty("food") is int f ? f : 0;
        var pheromone = patch.GetProperty("pheromone") is double p ? Math.Round(p, 2) : 0.0;
        var nest = patch.GetProperty("nest") is true;
        var eaten = patch.GetProperty("grass") is false;

        if (food <= 0 && pheromone <= 0 && !nest && !eaten)
        {
            return null;
        }

        return new PatchDTO
        {
            X = patch.X,
            Y = patch.Y,
            Food = Math.Max(food, 0),
            Pheromone = Math.Max(pheromone, 0),
            Nest = nest,
            Grass = eaten ? false : null
        };
    }

    // Returns the run and a copy of its ticks so far, or null if runId is not
    // the current run (it was reset or replaced since the page last saw it).
    public (RunInfo Run, IReadOnlyList<TickStatistics> Ticks)? GetTickHistory(int runId)
    {
        var activeRun = _activeRun;
        if (activeRun is null || activeRun.Info.RunId != runId) return null;

        lock (activeRun.Ticks)
        {
            return (activeRun.Info, activeRun.Ticks.ToList());
        }
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
