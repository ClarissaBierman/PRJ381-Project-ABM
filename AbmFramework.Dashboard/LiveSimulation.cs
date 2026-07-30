using AbmFramework.Config;
using AbmFramework.Dashboard.Hubs;
using AbmFramework.Dashboard.Models;
using AbmFramework.Engine;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard;

public class LiveSimulationService : BackgroundService
{
    private readonly IHubContext<SimulationHub> _hubContext;

    public LiveSimulationService(IHubContext<SimulationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new SimulationConfig
        {
            ScenarioName = "Dashboard Live Demo",
            GridWidth = 20,
            GridHeight = 20,
            Topology = GridTopology.Bounded,
            AgentCount = 200,
            InitialInfected = 5,
            InfectionProbability = 0.02,

            RecoveryTicks = 50,
            TickLimit = 500,
            Scheduler = SchedulerKind.Random
        };

        var engine = new SimulationEngine();

        engine.TickCompleted += async (sender, stats) =>
        {
            var tickData = new TickData
            {
                Tick = stats.Tick,
                Susceptible = stats.Susceptible,
                Infected = stats.Infected,
                Recovered = stats.Recovered
            };

            await _hubContext.Clients.All.SendAsync("ReceiveTick", tickData, cancellationToken: stoppingToken);

            var snapshot = engine.GetSnapshot();
            var agentDtos = snapshot.Agents.Select(a => new AgentDTO
            {
                Id = a.Id,
                X = a.Position.X,
                Y = a.Position.Y,
                State = a.State.ToString()
            }).ToList();

            await _hubContext.Clients.All.SendAsync("ReceiveGrid", agentDtos, cancellationToken: stoppingToken);
        };

        await engine.StartAsync(config, stoppingToken);
    }
}
