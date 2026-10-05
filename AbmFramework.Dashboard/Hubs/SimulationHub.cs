using AbmFramework.Config;
using AbmFramework.Dashboard.Models;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard.Hubs;

public class SimulationHub : Hub
{
    private readonly LiveSimulationService _simulation;

    public SimulationHub(LiveSimulationService simulation)
    {
        _simulation = simulation;
    }

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

    public IReadOnlyList<ModelOption> GetModels() => ModelCatalog.Options;

    public IReadOnlyDictionary<string, SimulationConfig> GetModelDefaults() =>
        Enum.GetValues<ModelType>().ToDictionary(model => model.ToString(), ModelCatalog.BuildConfig);

    public Task StartSimulation(SimulationConfig config) => Begin(config, startPaused: false);

    public Task SetupSimulation(SimulationConfig config) => Begin(config, startPaused: true);

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

    public Task PauseSimulation() => _simulation.PauseAsync();

    public Task ResumeSimulation() => _simulation.ResumeAsync();

    public Task StepSimulation() => _simulation.StepAsync();

    public Task ResetSimulation() => _simulation.ResetAsync();

    public Task SetSpeed(int tickDelayMs) => _simulation.SetSpeedAsync(tickDelayMs);
}
