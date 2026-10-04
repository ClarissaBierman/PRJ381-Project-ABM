using System.Text;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

[ApiController]
[Route("api/simulation")]
public class SimulationController : ControllerBase
{
    private readonly LiveSimulationService _simulation;

    public SimulationController(LiveSimulationService simulation)
    {
        _simulation = simulation;
    }

    // Downloads the tick-by-tick statistics of the given run so far. runId
    // guards against downloading a different run than the one on screen if
    // the simulation was restarted or reset in the meantime.
    [HttpGet("ticks.csv")]
    public async Task<IActionResult> DownloadTicksCsv([FromQuery] int runId)
    {
        if (_simulation.GetTickHistory(runId) is not { } history)
        {
            return NotFound("That run is no longer available. It may have been reset or restarted.");
        }

        var records = history.Ticks.Select(stats => new TickRecord
        {
            TickNumber = stats.Tick,
            Susceptible = stats.Susceptible,
            Infected = stats.Infected,
            Recovered = stats.Recovered,
            Metrics = stats.Metrics
        });

        using var writer = new StringWriter();
        await CsvExporter.WriteTicksAsync(records, writer);

        var fileName = $"{history.Run.Model}-run{history.Run.RunId}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(writer.ToString()), "text/csv", fileName);
    }
}
