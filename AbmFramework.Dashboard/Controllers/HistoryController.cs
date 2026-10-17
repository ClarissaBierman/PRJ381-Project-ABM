using System.Text;
using System.Text.Json.Nodes;
using ABM.Persistence;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using AbmFramework.Config;
using AbmFramework.Dashboard.History;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

// Past runs: saved live runs and Compare batches, read back from the database.
[ApiController]
[Route("api/history")]
public class HistoryController : ControllerBase
{
    private readonly HistoryWriter _history;
    private readonly LiveSimulationService _simulation;

    public HistoryController(HistoryWriter history, LiveSimulationService simulation)
    {
        _history = history;
        _simulation = simulation;
    }

    private IRepository Repository => _history.Repository!;

    private IActionResult? Unavailable() => _history.Enabled
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, _history.Problem ?? "Saving is turned off.");

    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        enabled = _history.Enabled,
        databasePath = Path.GetFullPath(_history.DatabasePath),
        problem = _history.Problem
    });

    // ---------------------------------------------------------------- runs

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

    [HttpDelete("compares/{groupId:guid}")]
    public async Task<IActionResult> DeleteCompare(Guid groupId)
    {
        if (Unavailable() is { } problem) return problem;

        await _history.WhenIdleAsync();
        return await Repository.DeleteBatchGroupAsync(groupId) > 0
            ? NoContent()
            : NotFound("That comparison is not in the database any more.");
    }

    private static string DisplayName(string model) =>
        Enum.TryParse<ModelType>(model, out var parsed) ? ModelCatalog.DisplayName(parsed) : model;

    private static string FileName(Simulation simulation, string extension) =>
        $"{simulation.Model}-{simulation.StartedAt.ToLocalTime():yyyyMMdd-HHmmss}.{extension}";
}
