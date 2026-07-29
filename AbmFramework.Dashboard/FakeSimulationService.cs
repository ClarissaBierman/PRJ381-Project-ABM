using AbmFramework.Dashboard.Hubs;
using AbmFramework.Dashboard.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace AbmFramework.Dashboard;

public class FakeSimulationService : BackgroundService
{
    private readonly IHubContext<SimulationHub> _hubContext;

    public FakeSimulationService(IHubContext<SimulationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rnd = new Random();
        int tick = 0;

        // Start with a population distribution
        double susceptible = 990;
        double infected = 10;
        double recovered = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            tick++;

            //fake SIR dynamics for demo purposes
            double newInfections = Math.Min(susceptible, infected * 0.05 + rnd.NextDouble() * 2);
            double newRecoveries = infected * 0.02 + rnd.NextDouble();

            susceptible = Math.Max(0, susceptible - newInfections);
            infected = Math.Max(0, infected + newInfections - newRecoveries);
            recovered = Math.Max(0, recovered + newRecoveries);

            var tickData = new TickData
            {
                Tick = tick,
                Susceptible = Math.Round(susceptible, 2),
                Infected = Math.Round(infected, 2),
                Recovered = Math.Round(recovered, 2)
            };

            // Broadcast to connected clients
            await _hubContext.Clients.All.SendAsync("ReceiveTick", tickData, cancellationToken: stoppingToken);

            try
            {
                await Task.Delay(500, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutdown
            }
        }
    }
}
