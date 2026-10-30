// =============================================================================
// LiveSimulation.cs  (AbmFramework.Dashboard)
//
// LiveSimulationService runs THE live simulation shown on the dashboard.
// There is only ever one live run at a time, shared by every open browser
// tab (they all see the same run).
//
// What it does:
//   - Start / Setup / Pause / Resume / Step / Reset / speed, called by SimulationHub.
//   - Runs the SimulationEngine on a background task (Task.Run).
//   - After every tick, sends the numbers ("ReceiveTick") and, at most about
//     25 times a second, the grid picture ("ReceiveGrid") to every browser.
//   - Hands every tick to a LiveRunRecorder, which saves the run to the database.
//   - Continue (from Past runs): see ContinueAsync below.
//
// How Continue works (the clever bit): we don't save the whole world every
// tick. We save the settings and the seed. Because the same seed always gives
// the same run, replaying the run from tick 0 as fast as possible lands on
// exactly the same moment it was stopped at. The page sees "Loading", then the
// run appears paused at the saved tick with its full chart history.
//
// Threads: hub calls arrive on web request threads, while ticks run on the
// engine's background thread. _lock makes sure two Start/Reset calls can't
// overlap, and the tick list is locked whenever it is read or written.
// =============================================================================

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

/// <summary>
/// Runs the dashboard's single live simulation and streams it to every browser.
/// Registered as a singleton in Program.cs.
/// </summary>
public class LiveSimulationService
{
    private readonly IHubContext<SimulationHub> _hubContext;
    private readonly ILogger<LiveSimulationService> _logger;
    private readonly HistoryWriter _history;
    // Only one Start/Continue/Reset at a time. (A SemaphoreSlim is used instead
    // of "lock" because you can't await inside a lock block.)
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SimulationEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    // Goes up by one for every run, so the page can ignore messages from an old run.
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

    /// <summary>Fastest speed the slider allows (milliseconds between ticks).</summary>
    public const int MinTickDelayMs = 10;

    /// <summary>Slowest speed the slider allows.</summary>
    public const int MaxTickDelayMs = 1000;

    /// <summary>The run on screen, or null if there isn't one.</summary>
    public RunInfo? CurrentRun => _activeRun?.Info;

    /// <summary>
    /// What the page shows next to the run: "Stopped", "Ready" (after Setup),
    /// "Loading" (replaying for Continue), "Running", "Paused", "Completed" or "Error".
    /// </summary>
    public string Status { get; private set; } = "Stopped";

    /// <summary>The current speed (milliseconds between ticks). Kept between runs.</summary>
    public int TickDelayMs { get; private set; } = 50;

    /// <summary>The latest grid picture, sent to pages that connect mid-run.</summary>
    public GridData? LastGrid => _lastGrid;

    /// <summary>Created once by ASP.NET Core. IHubContext lets this service send to every browser.</summary>
    public LiveSimulationService(IHubContext<SimulationHub> hubContext, ILogger<LiveSimulationService> logger, HistoryWriter history)
    {
        _hubContext = hubContext;
        _logger = logger;
        _history = history;
    }

    /// <summary>The database id of the run on screen, if it is being saved.</summary>
    public Guid? CurrentHistoryId => _activeRun?.Recorder?.SimulationId;

    /// <summary>Called when the dashboard is closing: saves what the current run has so far.</summary>
    public void SaveForShutdown()
    {
        _activeRun?.Recorder?.Finish("Stopped", null);
    }

    /// <summary>
    /// Writes the ticks the current run has buffered, so a read straight
    /// afterwards (for example a download) sees everything up to now.
    /// </summary>
    public Task FlushHistoryAsync()
    {
        _activeRun?.Recorder?.Flush();
        return _history.WhenIdleAsync();
    }

    /// <summary>
    /// Starts a new live run, replacing any current one. startPaused is the
    /// dashboard's Setup button: agents are created and drawn at tick 0, then
    /// the run waits for Run or Step.
    /// </summary>
    /// <exception cref="ConfigValidationException">If the settings are invalid.</exception>
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

    /// <summary>
    /// Continues a run from Past runs by replaying it (same settings and seed)
    /// up to the tick where it stopped, then pausing there. See the file header.
    /// </summary>
    /// <returns>A message for the user if it can't be continued, or null on success.</returns>
    public async Task<string?> ContinueAsync(Guid historyId)
    {
        if (!_history.Enabled) return _history.Problem ?? "Saving is turned off, so there is nothing to continue.";
        if (historyId == CurrentHistoryId) return "That run is already on screen. Press Run to carry on.";

        // Make sure every queued write has landed before reading the run back.
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
        // The last saved tick, to check afterwards that the replay really
        // arrived at the same numbers.
        var lastSaved = ticks.LastOrDefault(t => t.TickNumber == saved.TickCount - 1);

        var recorder = LiveRunRecorder.Continue(_history, config, historyId, environment?.Id ?? Guid.NewGuid(), saved.TickCount);
        await StartCoreAsync(config, false, recorder, new ContinueFrom(saved.TickCount, lastSaved));
        return null;
    }

    // Shared by StartAsync and ContinueAsync: stops the current run, builds a
    // new engine, wires up its events and starts it on a background task.
    // continuing: the recorder to keep saving into (Continue), or null for a new run.
    // replay: where to stop replaying (Continue), or null for a new run.
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

            // Tick 0 exists: save the starting layout and draw it (not during a
            // replay, where the page should only see the end result).
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
                // Only for a run that finished by itself, not one that was
                // reset or replaced (that is handled in StopCurrentAsync).
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

            // Run the engine on a background thread so this method returns at once.
            _runTask = Task.Run(() => RunAsync(engine, config, recorder, cts.Token));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Pauses the run, saves what it has so far and sends the latest grid.</summary>
    public async Task PauseAsync()
    {
        if (_engine is null || !_engine.IsRunning) return;

        _engine.Pause();
        _activeRun?.Recorder?.Flush();
        if (_lastGrid is { } grid) await BroadcastGridAsync(grid, CancellationToken.None);
        await SetStatusAsync("Paused");
    }

    /// <summary>Carries on after a pause.</summary>
    public async Task ResumeAsync()
    {
        if (_engine is null || !_engine.IsPaused) return;

        _engine.Resume();
        await SetStatusAsync("Running");
    }

    /// <summary>Runs one tick while paused.</summary>
    public async Task StepAsync()
    {
        if (_engine is null || !_engine.IsPaused) return;

        _engine.Step();
        if (Status == "Ready")
        {
            await SetStatusAsync("Paused");
        }
    }

    /// <summary>Changes the speed for this and future runs, and tells every page.</summary>
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

    /// <summary>Stops and clears the current run. The page goes back to empty.</summary>
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

    // The background task: runs the engine until it ends. Cancelling is the
    // normal way a run stops, so that exception is ignored. Anything else is
    // a real error: it is logged and the page shows "Error".
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

    // Cancels the current run, waits for its task to end, saves it as
    // "Stopped" and clears everything. Callers must hold _lock.
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

    // A snapshot, or null if the engine has nothing to snapshot (yet).
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

    // Called after every tick. Runs synchronously inside the engine's tick
    // loop, so the agent list is copied (BuildGridData) before the next tick
    // starts moving agents around. Keep this fast: the engine waits for it.
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

        // Replaying for Continue: send nothing until we reach the saved tick.
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

        // Throttle the grid (but never the numbers): see MinGridIntervalMs.
        // When paused, always send it so the page shows the exact tick.
        var now = Environment.TickCount64;
        var sendGrid = engine.IsPaused || now - _lastGridSentMs >= MinGridIntervalMs;
        if (sendGrid) _lastGridSentMs = now;

        // "_ =" sends without waiting, so the engine isn't held up by the network.
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

            // If the model code changed since the run was saved, the replay can
            // end up somewhere slightly different. Warn the user if so.
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

    // True if a replayed tick has the same numbers as the saved one.
    private static bool SameResult(TickStatistics stats, TickRecord saved) =>
        stats.Susceptible == saved.Susceptible &&
        stats.Infected == saved.Infected &&
        stats.Recovered == saved.Recovered &&
        stats.Metrics.Count == saved.Metrics.Count &&
        stats.Metrics.All(m => saved.Metrics.TryGetValue(m.Key, out var v) && Math.Abs(v - m.Value) < 1e-9);

    // Turns a snapshot into the GridData message: every agent with its state
    // (plus energy for Wolf-Sheep, heading and speed for Boids), and every
    // patch that has something to colour.
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

    /// <summary>
    /// Returns the run and a copy of its ticks so far, or null if runId is not
    /// the current run (it was reset or replaced since the page last saw it).
    /// Used by the Download button.
    /// </summary>
    public (RunInfo Run, IReadOnlyList<TickStatistics> Ticks)? GetTickHistory(int runId)
    {
        var activeRun = _activeRun;
        if (activeRun is null || activeRun.Info.RunId != runId) return null;

        lock (activeRun.Ticks)
        {
            return (activeRun.Info, activeRun.Ticks.ToList());
        }
    }

    // Sends one tick (and maybe the grid) to every page. Errors are logged,
    // never thrown, because nobody is awaiting this.
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

    // Sends just the grid to every page.
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

    // Updates Status and tells every page.
    private async Task SetStatusAsync(string status)
    {
        Status = status;
        await _hubContext.Clients.All.SendAsync("SimulationStatus", status);
    }
}
