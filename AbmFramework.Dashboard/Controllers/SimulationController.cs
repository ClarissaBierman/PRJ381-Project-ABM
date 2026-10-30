// =============================================================================
// SimulationController.cs  (AbmFramework.Dashboard.Controllers)
//
// Downloads for the run that is on screen right now (the Download button):
//   GET /api/simulation/ticks.xlsx?runId=3   every tick so far, as Excel
//   GET /api/simulation/ticks.csv?runId=3    the same, as CSV
//
// These are plain HTTP (REST) endpoints rather than SignalR messages because
// the browser can download a file straight from a URL.
// Saved runs are downloaded through HistoryController instead.
// =============================================================================

using System.Text;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

/// <summary>Downloads of the live run's tick statistics.</summary>
[ApiController]
[Route("api/simulation")]
public class SimulationController : ControllerBase
{
    private readonly LiveSimulationService _simulation;

    /// <summary>ASP.NET Core passes in the live simulation service.</summary>
    public SimulationController(LiveSimulationService simulation)
    {
        _simulation = simulation;
    }

    /// <summary>The same as ticks.csv, as an Excel file that opens straight into columns.</summary>
    [HttpGet("ticks.xlsx")]
    public IActionResult DownloadTicksExcel([FromQuery] int runId)
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

        var fileName = $"{history.Run.Model}-run{history.Run.RunId}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
        return File(ExcelExporter.WriteTicks(records), ExcelExporter.ContentType, fileName);
    }

    /// <summary>
    /// Downloads the tick-by-tick statistics of the given run so far. runId
    /// guards against downloading a different run than the one on screen if
    /// the simulation was restarted or reset in the meantime.
    /// </summary>
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
