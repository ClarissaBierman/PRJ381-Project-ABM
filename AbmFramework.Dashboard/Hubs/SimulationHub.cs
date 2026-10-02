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

        await Clients.Caller.SendAsync("SimulationStatus", _simulation.Status);
        await Clients.Caller.SendAsync("SpeedChanged", _simulation.TickDelayMs);
        await base.OnConnectedAsync();
    }

    public IReadOnlyList<ModelOption> GetModels() => ModelCatalog.Options;

    public Task StartSimulation(string model)
    {
        if (!Enum.TryParse<ModelType>(model, ignoreCase: true, out var parsed))
        {
            throw new HubException($"Unknown model '{model}'.");
        }

        return _simulation.StartAsync(parsed);
    }

    public Task PauseSimulation() => _simulation.PauseAsync();

    public Task ResumeSimulation() => _simulation.ResumeAsync();

    public Task ResetSimulation() => _simulation.ResetAsync();

    public Task SetSpeed(int tickDelayMs) => _simulation.SetSpeedAsync(tickDelayMs);
}
