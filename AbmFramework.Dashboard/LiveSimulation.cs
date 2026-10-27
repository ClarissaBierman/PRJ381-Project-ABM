using System.Text.Json;
using ABM.Core;
using ABM.Persistence.Models;
using AbmFramework.Config;
using AbmFramework.Dashboard.History;
using AbmFramework.Dashboard.Hubs;
using AbmFramework.Dashboard.Models;
using AbmFramework.Engine;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard;

public class LiveSimulationService
{
    private readonly IHubContext<SimulationHub> _hubContext;
    private readonly ILogger<LiveSimulationService> _logger;
    private readonly HistoryWriter _history;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SimulationEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _nextRunId = 1;

    // The current run together with every tick it has produced so far, kept
    // in one object so the CSV download can never pair one run's info with
    // another run's ticks. Ticks are appended on the engine's thread and read
    // by download requests, so access to the list is locked on the list.
    private sealed record ActiveRun(RunInfo Info, List<TickStatistics> Ticks, LiveRunRecorder? Recorder, ContinueFrom? Replay);

    // A saved run being continued: replay its first SavedTicks ticks, then pause there.
    private sealed record ContinueFrom(int SavedTicks, TickRecord? LastSaved);
    private ActiveRun? _activeRun;
    private GridData? _lastGrid;

    // At fast speeds a big grid can't be sent and drawn 100 times a second, so the
    // grid is sent at most about 25 times a second. Every tick's numbers are still sent.
    private const int MinGridIntervalMs = 40;
    private long _lastGridSentMs;

    public const int MinTickDelayMs = 10;
    public const int MaxTickDelayMs = 1000;

    public RunInfo? CurrentRun => _activeRun?.Info;
    public string Status { get; private set; } = "Stopped";
    public int TickDelayMs { get; private set; } = 50;
    public GridData? LastGrid => _lastGrid;

    public LiveSimulationService(IHubContext<SimulationHub> hubContext, ILogger<LiveSimulationService> logger, HistoryWriter history)
    {
        _hubContext = hubContext;
        _logger = logger;
        _history = history;
    }

    // The database id of the run on screen, if it is being saved.
    public Guid? CurrentHistoryId => _activeRun?.Recorder?.SimulationId;

    // Called when the dashboard is closing: saves what the current run has so far.
    public void SaveForShutdown()
    {
        _activeRun?.Recorder?.Finish("Stopped", null);
    }

    // Writes the ticks the current run has buffered, so a read straight
    // afterwards sees everything up to now.
    public Task FlushHistoryAsync()
    {
        _activeRun?.Recorder?.Flush();
        return _history.WhenIdleAsync();
    }

    // startPaused is the dashboard's Setup button: agents are created and drawn
    // at tick 0, then the run waits for Go or Go once.
    public Task StartAsync(SimulationConfig config, bool startPaused = false)
    {
        ConfigLoader.Validate(config);

        // Every run gets a seed, even with the box left empty, so a saved run
        // can always be replayed and continued later.
        if (config.RandomSeed is null)
        {
            config = config.Clone();
            config.RandomSeed = Random.Shared.Next();
        }

        return StartCoreAsync(config, startPaused, null, null);
    }

    // Continue on a run from Past runs. Returns a message if it can't be continued.
    public async Task<string?> ContinueAsync(Guid historyId)
    {
        if (!_history.Enabled) return _history.Problem ?? "Saving is turned off, so there is nothing to continue.";
        if (historyId == CurrentHistoryId) return "That run is already on screen. Press Run to carry on.";

        await _history.WhenIdleAsync();
        var repository = _history.Repository!;
        var saved = await repository.GetSimulationAsync(historyId);
        if (saved is null || saved.Source != "Live") return "That run is not in the database any more.";
        if (saved.Seed is null) return "This run was saved without a seed, so it can't be replayed exactly.";
        if (saved.TickCount <= 0) return "This run has no ticks to continue from.";

        SimulationConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<SimulationConfig>(saved.ConfigurationJson, HistoryWriter.Json);
        }
        catch (JsonException)
        {
            config = null;
        }

        if (config is null) return "The saved settings for this run could not be read.";
        if (saved.TickCount >= config.TickLimit) return "This run already reached the end of its run length.";

        config.RandomSeed = saved.Seed;
        try
        {
            ConfigLoader.Validate(config);
        }
        catch (ConfigValidationException ex)
        {
            return string.Join(" ", ex.Errors);
        }

        var environment = await repository.GetEnvironmentAsync(historyId);
        var ticks = await repository.GetTicksAsync(historyId);
        var lastSaved = ticks.LastOrDefault(t => t.TickNumber == saved.TickCount - 1);

        var recorder = LiveRunRecorder.Continue(_history, config, historyId, environment?.Id ?? Guid.NewGuid(), saved.TickCount);
        await StartCoreAsync(config, false, recorder, new ContinueFrom(saved.TickCount, lastSaved));
        return null;
    }

    private async Task StartCoreAsync(SimulationConfig config, bool startPaused, LiveRunRecorder? continuing, ContinueFrom? replay)
    {
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

            var recorder = continuing ?? (_history.Enabled ? new LiveRunRecorder(_history, config) : null);
            run.HistoryId = recorder?.SimulationId.ToString();

            var activeRun = new ActiveRun(run, new List<TickStatistics>(), recorder, replay);

            // While replaying a continued run there is no delay between ticks.
            var engine = new SimulationEngine { TickDelayMs = replay is null ? TickDelayMs : 0, StartPaused = startPaused };
            var cts = new CancellationTokenSource();

            engine.SimulationInitialised += (sender, e) =>
            {
                var snapshot = engine.GetSnapshot();
                recorder?.OnInitialised(snapshot);
                if (replay is not null) return;

                var grid = BuildGridData(snapshot, run.RunId);
                _lastGrid = grid;
                _ = BroadcastGridAsync(grid, cts.Token);
            };

            engine.TickCompleted += (sender, stats) => OnTick(engine, activeRun, stats, cts.Token);
            engine.SimulationCompleted += (sender, e) =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(engine, _engine))
                {
                    recorder?.Finish("Completed", engine.GetSnapshot());
                    if (_lastGrid is { } finalGrid) _ = BroadcastGridAsync(finalGrid, cts.Token);
                    _ = SetStatusAsync("Completed");
                }
            };

            _engine = engine;
            _cts = cts;
            _activeRun = activeRun;
            _lastGrid = null;

            await _hubContext.Clients.All.SendAsync("SimulationStarted", run);
            await SetStatusAsync(replay is not null ? "Loading" : startPaused ? "Ready" : "Running");

            _runTask = Task.Run(() => RunAsync(engine, config, recorder, cts.Token));
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
        _activeRun?.Recorder?.Flush();
        if (_lastGrid is { } grid) await BroadcastGridAsync(grid, CancellationToken.None);
        await SetStatusAsync("Paused");
    }

    public async Task ResumeAsync()
    {
        if (_engine is null || !_engine.IsPaused) return;

        _engine.Resume();
        await SetStatusAsync("Running");
    }

    public async Task StepAsync()
    {
        if (_engine is null || !_engine.IsPaused) return;

        _engine.Step();
        if (Status == "Ready")
        {
            await SetStatusAsync("Paused");
        }
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
            _lastGrid = null;

            await _hubContext.Clients.All.SendAsync("SimulationReset");
            await SetStatusAsync("Stopped");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunAsync(SimulationEngine engine, SimulationConfig config, LiveRunRecorder? recorder, CancellationToken token)
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
            recorder?.Finish("Error", null);
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

        if (_activeRun?.Recorder is { } recorder)
        {
            recorder.Finish("Stopped", TrySnapshot(_engine));
        }

        _engine?.Reset();
        _cts.Dispose();

        _engine = null;
        _cts = null;
        _runTask = null;
    }

    private static SimulationSnapshot? TrySnapshot(SimulationEngine? engine)
    {
        try
        {
            return engine?.GetSnapshot();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

        var snapshot = engine.GetSnapshot();
        activeRun.Recorder?.OnTick(stats, snapshot);

        if (activeRun.Replay is { } replay && stats.Tick < replay.SavedTicks)
        {
            if (stats.Tick < replay.SavedTicks - 1) return;

            // Back where the saved run stopped: pause here and show it.
            engine.Pause();
            engine.TickDelayMs = TickDelayMs;
            var reached = BuildGridData(snapshot, runId);
            _lastGrid = reached;
            _ = FinishReplayAsync(activeRun, reached, stats, replay.LastSaved, token);
            return;
        }

        var grid = BuildGridData(snapshot, runId);
        _lastGrid = grid;

        var now = Environment.TickCount64;
        var sendGrid = engine.IsPaused || now - _lastGridSentMs >= MinGridIntervalMs;
        if (sendGrid) _lastGridSentMs = now;

        _ = BroadcastAsync(tick, sendGrid ? grid : null, token);
    }

    // Sends the replayed ticks to the page in one go, then the grid, then pauses.
    private async Task FinishReplayAsync(ActiveRun activeRun, GridData grid, TickStatistics reached, TickRecord? lastSaved, CancellationToken token)
    {
        try
        {
            List<TickData> history;
            lock (activeRun.Ticks)
            {
                history = activeRun.Ticks.Select(s => new TickData
                {
                    RunId = activeRun.Info.RunId,
                    Tick = s.Tick,
                    Susceptible = s.Susceptible,
                    Infected = s.Infected,
                    Recovered = s.Recovered,
                    Metrics = s.Metrics
                }).ToList();
            }

            await _hubContext.Clients.All.SendAsync("ReceiveTickHistory", history, token);
            await _hubContext.Clients.All.SendAsync("ReceiveGrid", grid, token);
            await SetStatusAsync("Paused");

            if (lastSaved is not null && !SameResult(reached, lastSaved))
            {
                _logger.LogWarning("Continued run {Id} did not replay to the same values", activeRun.Recorder?.SimulationId);
                await _hubContext.Clients.All.SendAsync("SimulationNotice",
                    "This run did not replay exactly the same as when it was saved (the model code may have changed), so it carries on from a slightly different point.",
                    token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show the continued run");
        }
    }

    private static bool SameResult(TickStatistics stats, TickRecord saved) =>
        stats.Susceptible == saved.Susceptible &&
        stats.Infected == saved.Infected &&
        stats.Recovered == saved.Recovered &&
        stats.Metrics.Count == saved.Metrics.Count &&
        stats.Metrics.All(m => saved.Metrics.TryGetValue(m.Key, out var v) && Math.Abs(v - m.Value) < 1e-9);

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
                },
                Heading = a is BoidAgent boid ? Math.Round(Math.Atan2(boid.VY, boid.VX), 3) : null,
                Speed = a is BoidAgent b ? Math.Round(Math.Sqrt(b.VX * b.VX + b.VY * b.VY), 2) : null
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

    private async Task BroadcastAsync(TickData tick, GridData? grid, CancellationToken token)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveTick", tick, token);
            if (grid is not null)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveGrid", grid, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast tick {Tick}", tick.Tick);
        }
    }

    private async Task BroadcastGridAsync(GridData grid, CancellationToken token)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveGrid", grid, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast the starting grid");
        }
    }

    private async Task SetStatusAsync(string status)
    {
        Status = status;
        await _hubContext.Clients.All.SendAsync("SimulationStatus", status);
    }
}
