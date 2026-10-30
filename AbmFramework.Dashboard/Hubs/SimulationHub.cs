// =============================================================================
// SimulationHub.cs  (AbmFramework.Dashboard.Hubs)
//
// The SignalR hub for the live simulation: the page calls these methods (for
// example connection.invoke("PauseSimulation")) and the server pushes updates
// back (ReceiveTick, ReceiveGrid, SimulationStatus, ...).
//
// The hub itself is thin. A new hub object is created for every call, so it
// can't keep any state. The real work, and the state, is in the single
// LiveSimulationService.
//
// Message names are plain strings on both sides. If you rename one here,
// rename it in wwwroot/index.html too.
// =============================================================================

using AbmFramework.Config;
using AbmFramework.Dashboard.Models;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard.Hubs;

/// <summary>The page's remote control for the live simulation.</summary>
public class SimulationHub : Hub
{
    private readonly LiveSimulationService _simulation;

    public SimulationHub(LiveSimulationService simulation)
    {
        _simulation = simulation;
    }

    /// <summary>
    /// When a page connects (or reconnects, or a second tab opens), send it
    /// the current run, grid, status and speed so it catches up straight away.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (_simulation.CurrentRun is { } run)
        {
            await Clients.Caller.SendAsync("SimulationStarted", run);
        }

        if (_simulation.LastGrid is { } grid)
        {
            await Clients.Caller.SendAsync("ReceiveGrid", grid);
        }

        await Clients.Caller.SendAsync("SimulationStatus", _simulation.Status);
        await Clients.Caller.SendAsync("SpeedChanged", _simulation.TickDelayMs);
        await base.OnConnectedAsync();
    }

    /// <summary>The models for the drop-down.</summary>
    public IReadOnlyList<ModelOption> GetModels() => ModelCatalog.Options;

    /// <summary>Every model's default settings, keyed by model name, to fill in the sliders.</summary>
    public IReadOnlyDictionary<string, SimulationConfig> GetModelDefaults() =>
        Enum.GetValues<ModelType>().ToDictionary(model => model.ToString(), ModelCatalog.BuildConfig);

    /// <summary>Starts a new run straight away (Run without Setup first).</summary>
    public Task StartSimulation(SimulationConfig config) => Begin(config, startPaused: false);

    /// <summary>The Setup button: builds a new run and waits paused at tick 0.</summary>
    public Task SetupSimulation(SimulationConfig config) => Begin(config, startPaused: true);

    // Checks the settings, then starts the run. A HubException's message is
    // sent to the page, which shows it to the user.
    private Task Begin(SimulationConfig config, bool startPaused)
    {
        if (!Enum.IsDefined(config.Model))
        {
            throw new HubException($"Unknown model '{config.Model}'.");
        }

        try
        {
            ConfigLoader.Validate(config);
        }
        catch (ConfigValidationException ex)
        {
            throw new HubException(string.Join("\n", ex.Errors));
        }

        return _simulation.StartAsync(config, startPaused);
    }

    /// <summary>Continues a run from Past runs. Returns a message to show if it can't, or null on success.</summary>
    public Task<string?> ContinueSimulation(string historyId) =>
        Guid.TryParse(historyId, out var id)
            ? _simulation.ContinueAsync(id)
            : Task.FromResult<string?>("That run could not be found.");

    /// <summary>The Pause button.</summary>
    public Task PauseSimulation() => _simulation.PauseAsync();

    /// <summary>The Run button while paused.</summary>
    public Task ResumeSimulation() => _simulation.ResumeAsync();

    /// <summary>The Step button: one tick while paused.</summary>
    public Task StepSimulation() => _simulation.StepAsync();

    /// <summary>The Reset button: stops and clears the current run.</summary>
    public Task ResetSimulation() => _simulation.ResetAsync();

    /// <summary>The speed slider: milliseconds between ticks.</summary>
    public Task SetSpeed(int tickDelayMs) => _simulation.SetSpeedAsync(tickDelayMs);
}
