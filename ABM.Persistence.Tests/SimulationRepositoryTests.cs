// =============================================================================
// SimulationRepositoryTests.cs  (ABM.Persistence.Tests)
//
// Integration tests for the SQLite repository and the exporters. Each test
// gets its own throw-away database file in the temp folder (made in the
// constructor, deleted in Dispose), so tests never affect each other or your
// real Past runs.
//
// How to run every test: in Visual Studio open Test > Test Explorer and click
// "Run All Tests", or run "dotnet test" in the solution folder.
// Tests use xUnit: [Fact] is one test, [Theory] + [InlineData] runs the same
// test once per data row. Most tests follow Arrange (set up), Act (do the
// thing), Assert (check the result).
// =============================================================================

using System.Text.Json.Nodes;
using ABM.Persistence;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ABM.Persistence.Tests;

/// <summary>Tests that runs, ticks, agents, batches and exports save and load correctly.</summary>
public class SimulationRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"abm-test-{Guid.NewGuid():N}.db");
    private readonly SimulationRepository _repository;

    public SimulationRepositoryTests()
    {
        _repository = new SimulationRepository(_path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static Simulation NewSimulation(string model = "WolfSheep", string source = "Live", DateTime? started = null) => new()
    {
        Id = Guid.NewGuid(),
        Model = model,
        ScenarioName = "Test",
        Source = source,
        Status = "Running",
        StartedAt = started ?? DateTime.UtcNow,
        Seed = 42,
        ConfigurationJson = "{\"model\":\"" + model + "\",\"gridWidth\":20}"
    };

    private static EnvironmentRecord NewEnvironment(Guid simulationId) => new()
    {
        Id = Guid.NewGuid(),
        SimulationId = simulationId,
        GridWidth = 20,
        GridHeight = 15,
        Topology = "Toroidal",
        Scheduler = "Random"
    };

    private static TickRecord WolfSheepTick(Guid simulationId, int tick) => new()
    {
        SimulationId = simulationId,
        TickNumber = tick,
        TotalPopulation = 100 - tick,
        Metrics = new Dictionary<string, double> { ["Sheep"] = 80 - tick, ["Wolves"] = 20, ["Grass"] = 312.5 }
    };

    private async Task<(Simulation Simulation, EnvironmentRecord Environment)> SaveLiveRunAsync()
    {
        var simulation = NewSimulation();
        var environment = NewEnvironment(simulation.Id);
        await _repository.SaveSimulationAsync(simulation);
        await _repository.SaveEnvironmentAsync(environment);
        await _repository.SaveTickRecordsAsync(Enumerable.Range(0, 5).Select(t => WolfSheepTick(simulation.Id, t)).ToList());
        await _repository.SaveAgentsAsync(new[]
        {
            new AgentRecord { SimulationId = simulation.Id, AgentId = 1, Breed = "Sheep", FirstTick = -1, LastTick = -1 },
            new AgentRecord { SimulationId = simulation.Id, AgentId = 2, Breed = "Wolf", FirstTick = -1, LastTick = -1 }
        });
        await _repository.SaveAgentStatesAsync(new[]
        {
            new AgentState { SimulationId = simulation.Id, TickNumber = -1, AgentId = 1, X = 3, Y = 4, State = "Sheep", PropertiesJson = "{\"energy\":5}" },
            new AgentState { SimulationId = simulation.Id, TickNumber = -1, AgentId = 2, X = 7, Y = 1, State = "Wolf" }
        });
        await _repository.SavePatchesAsync(new[]
        {
            new PatchRecord { EnvironmentId = environment.Id, TickNumber = -1, X = 0, Y = 0, PropertiesJson = "{\"grass\":true}" },
            new PatchRecord { EnvironmentId = environment.Id, TickNumber = -1, X = 1, Y = 0, PropertiesJson = "{\"grass\":false}" }
        });
        return (simulation, environment);
    }

    [Fact]
    public async Task Initialize_CreatesAllTablesAndSetsTheSchemaVersion()
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        var tables = new List<string>();
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        foreach (var table in new[] { "Agents", "AgentStates", "Batches", "Environments", "Patches", "Simulations", "TickRecords" })
        {
            Assert.Contains(table, tables);
        }

        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Initialize_RebuildsAnOldSirOnlyDatabase()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), $"abm-old-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={oldPath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    @"CREATE TABLE Simulations (Id TEXT PRIMARY KEY, StartedAt TEXT NOT NULL, EndedAt TEXT, ScenarioName TEXT NOT NULL, ConfigurationJson TEXT NOT NULL);
                      CREATE TABLE TickRecords (SimulationId TEXT NOT NULL, TickNumber INTEGER NOT NULL, Susceptible INTEGER NOT NULL, Infected INTEGER NOT NULL, Recovered INTEGER NOT NULL);";
                command.ExecuteNonQuery();
            }

            var repository = new SimulationRepository(oldPath);
            var simulation = NewSimulation("SIR");
            await repository.SaveSimulationAsync(simulation);

            Assert.NotNull(await repository.GetSimulationAsync(simulation.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { oldPath, oldPath + "-wal", oldPath + "-shm" })
            {
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    [Fact]
    public async Task LiveRun_RoundTripsEveryTable()
    {
        var (simulation, environment) = await SaveLiveRunAsync();

        var loaded = await _repository.GetSimulationAsync(simulation.Id);
        Assert.NotNull(loaded);
        Assert.Equal("WolfSheep", loaded!.Model);
        Assert.Equal("Live", loaded.Source);
        Assert.Equal(42, loaded.Seed);
        Assert.Equal(simulation.ConfigurationJson, loaded.ConfigurationJson);
        Assert.Null(loaded.BatchId);

        var env = await _repository.GetEnvironmentAsync(simulation.Id);
        Assert.Equal(20, env!.GridWidth);
        Assert.Equal(15, env.GridHeight);
        Assert.Equal("Toroidal", env.Topology);

        var ticks = await _repository.GetTicksAsync(simulation.Id);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ticks.Select(t => t.TickNumber).ToArray());
        Assert.Equal(78.0, ticks[2].Metrics["Sheep"]);
        Assert.Equal(312.5, ticks[2].Metrics["Grass"]);
        Assert.Equal(98, ticks[2].TotalPopulation);

        var agents = await _repository.GetAgentsAsync(simulation.Id);
        Assert.Equal(new[] { "Sheep", "Wolf" }, agents.Select(a => a.Breed).ToArray());

        var states = await _repository.GetAgentStatesAsync(simulation.Id);
        Assert.Equal(2, states.Length);
        Assert.Equal("{\"energy\":5}", states[0].PropertiesJson);
        Assert.Null(states[1].PropertiesJson);

        var patches = await _repository.GetPatchesAsync(environment.Id);
        Assert.Equal(2, patches.Length);
        Assert.Equal("{\"grass\":false}", patches[1].PropertiesJson);
    }

    [Fact]
    public async Task SirTicks_KeepTheirCounts()
    {
        var simulation = NewSimulation("SIR");
        await _repository.SaveSimulationAsync(simulation);
        await _repository.SaveTickRecordAsync(new TickRecord
        {
            SimulationId = simulation.Id, TickNumber = 0, Susceptible = 190, Infected = 8, Recovered = 2, TotalPopulation = 200
        });

        var tick = Assert.Single(await _repository.GetTicksAsync(simulation.Id));
        Assert.Equal(190, tick.Susceptible);
        Assert.Equal(8, tick.Infected);
        Assert.Equal(2, tick.Recovered);
        Assert.Empty(tick.Metrics);
    }

    [Fact]
    public async Task UpdateSimulation_ChangesStatusEndTimeAndTickCount()
    {
        var simulation = NewSimulation();
        await _repository.SaveSimulationAsync(simulation);

        var ended = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);
        await _repository.UpdateSimulationAsync(simulation.Id, "Completed", ended, 500);

        var loaded = await _repository.GetSimulationAsync(simulation.Id);
        Assert.Equal("Completed", loaded!.Status);
        Assert.Equal(500, loaded.TickCount);
        Assert.Equal(ended, loaded.EndedAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task CloseUnfinishedRuns_OnlyChangesRunsStillMarkedRunning()
    {
        var running = NewSimulation();
        var done = NewSimulation();
        done.Status = "Completed";
        await _repository.SaveSimulationAsync(running);
        await _repository.SaveSimulationAsync(done);

        Assert.Equal(1, await _repository.CloseUnfinishedRunsAsync("Stopped"));

        var closed = await _repository.GetSimulationAsync(running.Id);
        Assert.Equal("Stopped", closed!.Status);
        Assert.NotNull(closed.EndedAt);
        Assert.Equal("Completed", (await _repository.GetSimulationAsync(done.Id))!.Status);
    }

    [Fact]
    public async Task SaveAgents_UpdatesLastTickButKeepsFirstTick()
    {
        var simulation = NewSimulation();
        await _repository.SaveSimulationAsync(simulation);

        await _repository.SaveAgentsAsync(new[] { new AgentRecord { SimulationId = simulation.Id, AgentId = 5, Breed = "Sheep", FirstTick = 3, LastTick = 3 } });
        await _repository.SaveAgentsAsync(new[] { new AgentRecord { SimulationId = simulation.Id, AgentId = 5, Breed = "Sheep", FirstTick = 99, LastTick = 40 } });

        var agent = Assert.Single(await _repository.GetAgentsAsync(simulation.Id));
        Assert.Equal(3, agent.FirstTick);
        Assert.Equal(40, agent.LastTick);
    }

    [Fact]
    public async Task ListSimulations_IsNewestFirstAndCanFilter()
    {
        var old = NewSimulation("SIR", started: DateTime.UtcNow.AddHours(-2));
        var middle = NewSimulation("WolfSheep", started: DateTime.UtcNow.AddHours(-1));
        var compareRun = NewSimulation("WolfSheep", source: "Compare");
        foreach (var s in new[] { old, middle, compareRun })
        {
            await _repository.SaveSimulationAsync(s);
        }
        await _repository.SaveEnvironmentAsync(NewEnvironment(middle.Id));

        var all = await _repository.ListSimulationsAsync();
        Assert.Equal(new[] { compareRun.Id, middle.Id, old.Id }, all.Select(r => r.Simulation.Id).ToArray());

        var live = await _repository.ListSimulationsAsync(source: "Live");
        Assert.Equal(new[] { middle.Id, old.Id }, live.Select(r => r.Simulation.Id).ToArray());
        Assert.Equal(20, live[0].GridWidth);
        Assert.Equal(0, live[1].GridWidth);

        var sir = await _repository.ListSimulationsAsync(model: "SIR");
        Assert.Equal(old.Id, Assert.Single(sir).Simulation.Id);
    }

    private async Task<Batch> SaveBatchAsync(Guid groupId, string label, int runs)
    {
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Label = label,
            Model = "WolfSheep",
            CreatedAt = DateTime.UtcNow,
            Runs = runs,
            BaseSeed = 7,
            TickLimit = 10,
            ConfigurationJson = "{\"model\":\"WolfSheep\"}",
            PageSettingsJson = "{\"initialSheep\":80}",
            SummaryJson = "{\"runCount\":" + runs + "}"
        };

        var batchRuns = Enumerable.Range(1, runs).Select(n =>
        {
            var simulation = NewSimulation(source: "Compare");
            simulation.BatchId = batch.Id;
            simulation.RunNumber = n;
            simulation.Status = "Completed";
            simulation.TickCount = 10;
            return new BatchRun
            {
                Simulation = simulation,
                Environment = NewEnvironment(simulation.Id),
                Ticks = Enumerable.Range(0, 10).Select(t => WolfSheepTick(simulation.Id, t)).ToList()
            };
        }).ToList();

        await _repository.SaveBatchAsync(batch, batchRuns);
        return batch;
    }

    [Fact]
    public async Task SaveBatch_StoresTheBatchAndEveryRunWithItsTicks()
    {
        var group = Guid.NewGuid();
        var a = await SaveBatchAsync(group, "A", 3);
        var b = await SaveBatchAsync(group, "B", 3);

        var loaded = await _repository.GetBatchGroupAsync(group);
        Assert.Equal(new[] { "A", "B" }, loaded.Select(x => x.Label).ToArray());
        Assert.Equal("{\"runCount\":3}", loaded[0].SummaryJson);
        Assert.Equal("{\"initialSheep\":80}", loaded[0].PageSettingsJson);

        var runs = await _repository.GetBatchRunsAsync(a.Id);
        Assert.Equal(new[] { 1, 2, 3 }, runs.Select(r => r.RunNumber!.Value).ToArray());
        Assert.All(runs, r => Assert.Equal(a.Id, r.BatchId));
        Assert.Equal(10, (await _repository.GetTicksAsync(runs[0].Id)).Length);
        Assert.NotNull(await _repository.GetEnvironmentAsync(runs[0].Id));

        var listed = await _repository.ListBatchesAsync();
        Assert.Equal(2, listed.Length);
        Assert.All(listed, x => Assert.Equal("", x.SummaryJson));
        Assert.Contains(listed, x => x.Id == b.Id);
    }

    [Fact]
    public async Task DeleteSimulation_RemovesEverythingThatBelongsToIt()
    {
        var (simulation, environment) = await SaveLiveRunAsync();
        var other = await SaveLiveRunAsync();

        Assert.True(await _repository.DeleteSimulationAsync(simulation.Id));

        Assert.Null(await _repository.GetSimulationAsync(simulation.Id));
        Assert.Null(await _repository.GetEnvironmentAsync(simulation.Id));
        Assert.Empty(await _repository.GetTicksAsync(simulation.Id));
        Assert.Empty(await _repository.GetAgentsAsync(simulation.Id));
        Assert.Empty(await _repository.GetAgentStatesAsync(simulation.Id));
        Assert.Empty(await _repository.GetPatchesAsync(environment.Id));

        Assert.Equal(5, (await _repository.GetTicksAsync(other.Simulation.Id)).Length);
        Assert.False(await _repository.DeleteSimulationAsync(simulation.Id));
    }

    [Fact]
    public async Task DeleteBatchGroup_RemovesBothSettingsAndTheirRuns()
    {
        var group = Guid.NewGuid();
        var a = await SaveBatchAsync(group, "A", 2);
        await SaveBatchAsync(group, "B", 2);
        var keep = await SaveBatchAsync(Guid.NewGuid(), "", 2);
        var runId = (await _repository.GetBatchRunsAsync(a.Id))[0].Id;

        Assert.Equal(2, await _repository.DeleteBatchGroupAsync(group));

        Assert.Empty(await _repository.GetBatchGroupAsync(group));
        Assert.Null(await _repository.GetSimulationAsync(runId));
        Assert.Empty(await _repository.GetTicksAsync(runId));
        Assert.Equal(2, (await _repository.GetBatchRunsAsync(keep.Id)).Length);
    }

    [Fact]
    public async Task JsonExport_WritesStoredJsonAsRealObjects()
    {
        var (simulation, environment) = await SaveLiveRunAsync();

        var json = JsonExporter.BuildRun(
            (await _repository.GetSimulationAsync(simulation.Id))!,
            await _repository.GetEnvironmentAsync(simulation.Id),
            await _repository.GetTicksAsync(simulation.Id),
            await _repository.GetAgentsAsync(simulation.Id),
            await _repository.GetAgentStatesAsync(simulation.Id),
            await _repository.GetPatchesAsync(environment.Id));

        var parsed = JsonNode.Parse(JsonExporter.Write(json))!;
        Assert.Equal(20, (int)parsed["configuration"]!["gridWidth"]!);
        Assert.Equal(15, (int)parsed["environment"]!["gridHeight"]!);
        Assert.Equal(5, parsed["ticks"]!.AsArray().Count);
        Assert.Equal(78.0, (double)parsed["ticks"]![2]!["Sheep"]!);
        Assert.Equal(5.0, (double)parsed["agentStates"]![0]!["properties"]!["energy"]!);
        Assert.False((bool)parsed["patches"]![1]!["properties"]!["grass"]!);
    }

    [Fact]
    public async Task CsvExport_FromTheDatabaseHasOneColumnPerMetric()
    {
        var (simulation, _) = await SaveLiveRunAsync();

        using var writer = new StringWriter();
        await CsvExporter.WriteTicksAsync(await _repository.GetTicksAsync(simulation.Id), writer);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Tick,Grass,Sheep,Wolves", lines[0]);
        Assert.Equal("0,312.5,80,20", lines[1]);
        Assert.Equal(6, lines.Length);
    }
}
