// =============================================================================
// HistoryController.cs  (AbmFramework.Dashboard.Controllers)
//
// The REST API behind the Past runs window (wwwroot/js/history.js):
//
//   Live runs
//     GET    /api/history/info                       is saving on, and where is the database
//     GET    /api/history/runs?model=SIR             list of saved live runs
//     GET    /api/history/runs/{id}                  one run with every tick (the View button)
//     GET    /api/history/runs/{id}/ticks.csv        download as CSV
//     GET    /api/history/runs/{id}/ticks.xlsx       download as Excel
//     GET    /api/history/runs/{id}/export.json      download everything as JSON
//     DELETE /api/history/runs/{id}                  delete a run
//   Comparisons
//     GET    /api/history/compares?model=SIR         list of saved comparisons
//     GET    /api/history/compares/{groupId}         one comparison (A and B)
//     GET    /api/history/compares/{groupId}/export.json
//     DELETE /api/history/compares/{groupId}
//
// Continue is NOT here: it goes through SimulationHub.ContinueSimulation,
// because it starts a live run.
//
// Before reading, most endpoints wait for queued writes to finish (FlushHistoryAsync
// / WhenIdleAsync), so you never see a run with half its ticks missing.
// =============================================================================

using System.Text;
using System.Text.Json.Nodes;
using ABM.Persistence;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using AbmFramework.Config;
using AbmFramework.Dashboard.History;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

/// <summary>Past runs: saved live runs and Compare batches, read back from the database.</summary>
[ApiController]
[Route("api/history")]
public class HistoryController : ControllerBase
{
    private readonly HistoryWriter _history;
    private readonly LiveSimulationService _simulation;

    /// <summary>ASP.NET Core passes in the database writer and the live simulation.</summary>
    public HistoryController(HistoryWriter history, LiveSimulationService simulation)
    {
        _history = history;
        _simulation = simulation;
    }

    private IRepository Repository => _history.Repository!;

    // Returns a "503 Service Unavailable" with the reason if saving is off,
    // otherwise null. Every endpoint checks this first.
    private IActionResult? Unavailable() => _history.Enabled
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, _history.Problem ?? "Saving is turned off.");

    /// <summary>Whether saving is on, where the database is, and why saving is off if it is.</summary>
    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        enabled = _history.Enabled,
        databasePath = Path.GetFullPath(_history.DatabasePath),
        problem = _history.Problem
    });

    // ---------------------------------------------------------------- runs

    /// <summary>
    /// The saved live runs, newest first, with whether each one can be
    /// continued and why not. Compare and console runs are not listed here.
    /// </summary>
    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery] string? model = null)
    {
        if (Unavailable() is { } problem) return problem;

        await _simulation.FlushHistoryAsync();
        var current = _simulation.CurrentHistoryId;
        var runs = await Repository.ListSimulationsAsync(source: "Live", model: string.IsNullOrEmpty(model) ? null : model);

        return Ok(runs.Select(r =>
        {
            var configuration = JsonNode.Parse(r.Simulation.ConfigurationJson);
            var tickLimit = (int?)configuration?["tickLimit"] ?? 0;
            var isCurrent = r.Simulation.Id == current;
            return new
            {
                id = r.Simulation.Id,
                model = r.Simulation.Model,
                displayName = DisplayName(r.Simulation.Model),
                status = isCurrent ? "On screen" : r.Simulation.Status,
                isCurrent,
                startedAt = r.Simulation.StartedAt,
                endedAt = r.Simulation.EndedAt,
                tickCount = r.Simulation.TickCount,
                seed = r.Simulation.Seed,
                gridWidth = r.GridWidth,
                gridHeight = r.GridHeight,
                tickLimit,
                canContinue = !isCurrent && r.Simulation.Seed is not null && r.Simulation.TickCount > 0 && r.Simulation.TickCount < tickLimit,
                continueHint = isCurrent ? "This run is already on screen"
                    : r.Simulation.Seed is null ? "Saved without a seed, so it can't be replayed"
                    : r.Simulation.TickCount >= tickLimit ? "This run already reached the end"
                    : $"Replay to tick {r.Simulation.TickCount} and carry on from there",
                configuration
            };
        }));
    }

    /// <summary>One saved run with every tick, for the View button's chart.</summary>
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid id)
    {
        if (Unavailable() is { } problem) return problem;

        if (id == _simulation.CurrentHistoryId) await _simulation.FlushHistoryAsync();

        var simulation = await Repository.GetSimulationAsync(id);
        if (simulation is null) return NotFound("That run is not in the database any more.");

        var environment = await Repository.GetEnvironmentAsync(id);
        var ticks = await Repository.GetTicksAsync(id);

        return Ok(new
        {
            id = simulation.Id,
            model = simulation.Model,
            displayName = DisplayName(simulation.Model),
            status = simulation.Status,
            startedAt = simulation.StartedAt,
            endedAt = simulation.EndedAt,
            tickCount = simulation.TickCount,
            seed = simulation.Seed,
            gridWidth = environment?.GridWidth,
            gridHeight = environment?.GridHeight,
            topology = environment?.Topology,
            scheduler = environment?.Scheduler,
            configuration = JsonNode.Parse(simulation.ConfigurationJson),
            ticks = ticks.Select(t => new
            {
                tick = t.TickNumber,
                values = t.Metrics.Count > 0
                    ? t.Metrics
                    : new Dictionary<string, double>
                    {
                        ["Susceptible"] = t.Susceptible,
                        ["Infected"] = t.Infected,
                        ["Recovered"] = t.Recovered
                    }
            })
        });
    }

    /// <summary>Downloads a saved run's ticks as CSV.</summary>
    [HttpGet("runs/{id:guid}/ticks.csv")]
    public async Task<IActionResult> DownloadRunCsv(Guid id)
    {
        if (Unavailable() is { } problem) return problem;

        if (id == _simulation.CurrentHistoryId) await _simulation.FlushHistoryAsync();

        var simulation = await Repository.GetSimulationAsync(id);
        if (simulation is null) return NotFound("That run is not in the database any more.");

        using var writer = new StringWriter();
        await CsvExporter.WriteTicksAsync(await Repository.GetTicksAsync(id), writer);
        return File(Encoding.UTF8.GetBytes(writer.ToString()), "text/csv", FileName(simulation, "csv"));
    }

    /// <summary>Downloads a saved run's ticks as Excel.</summary>
    [HttpGet("runs/{id:guid}/ticks.xlsx")]
    public async Task<IActionResult> DownloadRunExcel(Guid id)
    {
        if (Unavailable() is { } problem) return problem;

        if (id == _simulation.CurrentHistoryId) await _simulation.FlushHistoryAsync();

        var simulation = await Repository.GetSimulationAsync(id);
        if (simulation is null) return NotFound("That run is not in the database any more.");

        var bytes = ExcelExporter.WriteTicks(await Repository.GetTicksAsync(id), simulation.Model);
        return File(bytes, ExcelExporter.ContentType, FileName(simulation, "xlsx"));
    }

    /// <summary>Downloads everything saved for a run (ticks, agents, states, patches) as JSON.</summary>
    [HttpGet("runs/{id:guid}/export.json")]
    public async Task<IActionResult> DownloadRunJson(Guid id)
    {
        if (Unavailable() is { } problem) return problem;

        if (id == _simulation.CurrentHistoryId) await _simulation.FlushHistoryAsync();

        var simulation = await Repository.GetSimulationAsync(id);
        if (simulation is null) return NotFound("That run is not in the database any more.");

        var environment = await Repository.GetEnvironmentAsync(id);
        var json = JsonExporter.BuildRun(
            simulation,
            environment,
            await Repository.GetTicksAsync(id),
            await Repository.GetAgentsAsync(id),
            await Repository.GetAgentStatesAsync(id),
            environment is null ? null : await Repository.GetPatchesAsync(environment.Id));

        return File(Encoding.UTF8.GetBytes(JsonExporter.Write(json)), "application/json", FileName(simulation, "json"));
    }

    /// <summary>Deletes a saved run. The run on screen can't be deleted.</summary>
    [HttpDelete("runs/{id:guid}")]
    public async Task<IActionResult> DeleteRun(Guid id)
    {
        if (Unavailable() is { } problem) return problem;

        if (id == _simulation.CurrentHistoryId)
            return Conflict("This run is still on screen. Press Reset or Setup first, then delete it.");

        await _history.WhenIdleAsync();
        return await Repository.DeleteSimulationAsync(id) ? NoContent() : NotFound("That run is not in the database any more.");
    }

    // ------------------------------------------------------------- compares

    /// <summary>The saved comparisons (A and B batches grouped together), newest first.</summary>
    [HttpGet("compares")]
    public async Task<IActionResult> ListCompares([FromQuery] string? model = null)
    {
        if (Unavailable() is { } problem) return problem;

        await _history.WhenIdleAsync();
        var batches = await Repository.ListBatchesAsync();

        var groups = batches
            .Where(b => string.IsNullOrEmpty(model) || b.Model == model)
            .GroupBy(b => b.GroupId)
            .Select(g =>
            {
                var first = g.OrderBy(b => b.Label).First();
                return new
                {
                    groupId = g.Key,
                    model = first.Model,
                    displayName = DisplayName(first.Model),
                    createdAt = g.Min(b => b.CreatedAt),
                    runs = first.Runs,
                    baseSeed = first.BaseSeed,
                    tickLimit = first.TickLimit,
                    gridWidth = (int?)JsonNode.Parse(first.ConfigurationJson)?["gridWidth"],
                    gridHeight = (int?)JsonNode.Parse(first.ConfigurationJson)?["gridHeight"],
                    labels = g.Select(b => b.Label).OrderBy(l => l).ToArray()
                };
            })
            .OrderByDescending(g => g.createdAt);

        return Ok(groups);
    }

    /// <summary>One saved comparison, with its summaries and settings, to show it again.</summary>
    [HttpGet("compares/{groupId:guid}")]
    public async Task<IActionResult> GetCompare(Guid groupId)
    {
        if (Unavailable() is { } problem) return problem;

        await _history.WhenIdleAsync();
        var batches = await Repository.GetBatchGroupAsync(groupId);
        if (batches.Length == 0) return NotFound("That comparison is not in the database any more.");

        return Ok(new
        {
            groupId,
            model = batches[0].Model,
            createdAt = batches.Min(b => b.CreatedAt),
            results = batches.Select(b => new
            {
                label = b.Label,
                summary = JsonNode.Parse(b.SummaryJson),
                pageSettings = b.PageSettingsJson is null ? null : JsonNode.Parse(b.PageSettingsJson),
                configuration = JsonNode.Parse(b.ConfigurationJson)
            })
        });
    }

    /// <summary>Downloads a whole comparison, including every run's ticks, as JSON.</summary>
    [HttpGet("compares/{groupId:guid}/export.json")]
    public async Task<IActionResult> DownloadCompareJson(Guid groupId)
    {
        if (Unavailable() is { } problem) return problem;

        await _history.WhenIdleAsync();
        var batches = await Repository.GetBatchGroupAsync(groupId);
        if (batches.Length == 0) return NotFound("That comparison is not in the database any more.");

        var exported = new JsonArray();
        foreach (var batch in batches)
        {
            var runs = new List<JsonObject>();
            foreach (var run in await Repository.GetBatchRunsAsync(batch.Id))
            {
                runs.Add(JsonExporter.BuildRun(run, await Repository.GetEnvironmentAsync(run.Id), await Repository.GetTicksAsync(run.Id)));
            }
            exported.Add(JsonExporter.BuildBatch(batch, runs));
        }

        var json = new JsonObject { ["groupId"] = groupId, ["batches"] = exported };
        var name = $"compare-{batches[0].Model}-{batches.Min(b => b.CreatedAt).ToLocalTime():yyyyMMdd-HHmmss}.json";
        return File(Encoding.UTF8.GetBytes(JsonExporter.Write(json)), "application/json", name);
    }

    /// <summary>Deletes a comparison (both batches and all their runs).</summary>
    [HttpDelete("compares/{groupId:guid}")]
    public async Task<IActionResult> DeleteCompare(Guid groupId)
    {
        if (Unavailable() is { } problem) return problem;

        await _history.WhenIdleAsync();
        return await Repository.DeleteBatchGroupAsync(groupId) > 0
            ? NoContent()
            : NotFound("That comparison is not in the database any more.");
    }

    // "WolfSheep" -> "Wolf-Sheep Predation".
    private static string DisplayName(string model) =>
        Enum.TryParse<ModelType>(model, out var parsed) ? ModelCatalog.DisplayName(parsed) : model;

    // e.g. "WolfSheep-20261030-141500.csv" (start time in local time).
    private static string FileName(Simulation simulation, string extension) =>
        $"{simulation.Model}-{simulation.StartedAt.ToLocalTime():yyyyMMdd-HHmmss}.{extension}";
}
