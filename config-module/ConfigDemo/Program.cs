using AbmFramework.Config;
using AbmFramework.Engine;
using ABM.Persistence;
using ABM.Persistence.Export;
using ABM.Persistence.Models;
using System.Text.Json;

var options = ParseArgs(args);

if (options.ConfigPath is null)
{
    Console.Error.WriteLine("Missing required flag: --config <path-to-scenario-file>");
    Console.Error.WriteLine("Example: dotnet run -- --config ../configs/sir-default.json");
    Console.Error.WriteLine("Example (batch): dotnet run -- --config ../configs/schelling-default.json --batch 20");
    return 1;
}

SimulationConfig config;
try
{
    config = ConfigLoader.LoadFromFile(options.ConfigPath);

    if (options.Seed is not null) config.RandomSeed = options.Seed;
    if (options.Ticks is not null) config.TickLimit = options.Ticks.Value;
    if (options.Agents is not null) config.AgentCount = options.Agents.Value;

    ConfigLoader.Validate(config);
}
catch (ConfigValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (Exception ex) when (ex is FileNotFoundException or NotSupportedException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

PrintSummary(config);

if (options.BatchReplicates is int replicateCount)
{
    return await RunBatchAsync(config, replicateCount);
}

return await RunSingleAsync(config);

// --- Single run: tick history, agents and agent states saved to SQLite, CSV export at the end. ---
static async Task<int> RunSingleAsync(SimulationConfig config)
{
    var dbPath = Path.Combine(AppContext.BaseDirectory, "simulations.db");
    var repository = new SimulationRepository(dbPath);

    var simulationId = Guid.NewGuid();

    await repository.SaveSimulationAsync(new Simulation
    {
        Id = simulationId,
        Model = config.Model.ToString(),
        ScenarioName = config.ScenarioName,
        Source = "Console",
        Status = "Running",
        StartedAt = DateTime.UtcNow,
        Seed = config.RandomSeed,
        ConfigurationJson = JsonSerializer.Serialize(config)
    });

    await repository.SaveEnvironmentAsync(new EnvironmentRecord
    {
        Id = Guid.NewGuid(),
        SimulationId = simulationId,
        GridWidth = config.GridWidth,
        GridHeight = config.GridHeight,
        Topology = config.Topology.ToString(),
        Scheduler = config.Scheduler.ToString()
    });

    var engine = new SimulationEngine();
    var ticks = new List<TickRecord>();
    var agents = new Dictionary<int, AgentRecord>();
    var states = new List<AgentState>();

    engine.TickCompleted += (sender, stats) =>
    {
        Console.WriteLine(stats);

        ticks.Add(new TickRecord
        {
            SimulationId = simulationId,
            TickNumber = stats.Tick,
            Susceptible = stats.Susceptible,
            Infected = stats.Infected,
            Recovered = stats.Recovered,
            TotalPopulation = stats.TotalPopulation,
            Metrics = stats.Metrics
        });

        var snapshot = engine.GetSnapshot();
        foreach (var agent in snapshot.Agents)
        {
            if (!agents.TryGetValue(agent.Id, out var record))
            {
                record = new AgentRecord
                {
                    SimulationId = simulationId,
                    AgentId = agent.Id,
                    Breed = agent.GetType().Name.Replace("Agent", ""),
                    FirstTick = stats.Tick
                };
                agents[agent.Id] = record;
            }
            record.LastTick = stats.Tick;
        }

        // Every 10th tick is enough to replay where the agents were.
        if (stats.Tick % 10 == 0)
        {
            states.AddRange(snapshot.Agents.Select(a => new AgentState
            {
                SimulationId = simulationId,
                TickNumber = stats.Tick,
                AgentId = a.Id,
                X = a.Position.X,
                Y = a.Position.Y,
                State = a.DisplayState
            }));
        }
    };

    engine.SimulationCompleted += (sender, e) =>
    {
        Console.WriteLine();
        Console.WriteLine("Simulation completed.");
    };

    await engine.StartAsync(config);

    await repository.SaveAgentsAsync(agents.Values.ToList());
    await repository.SaveTickRecordsAsync(ticks);
    await repository.SaveAgentStatesAsync(states);
    await repository.UpdateSimulationAsync(simulationId, "Completed", DateTime.UtcNow, ticks.Count);
    Console.WriteLine($"Saved to the database: {repository.DatabasePath}");

    // Export the full tick history to CSV for external analysis
    var saved = await repository.GetTicksAsync(simulationId);
    var csvPath = Path.Combine(AppContext.BaseDirectory, $"simulation-{simulationId}.csv");
    await CsvExporter.ExportTicksAsync(saved, csvPath);
    Console.WriteLine($"Results exported to: {csvPath}");

    return 0;
}

// --- Batch run: Monte Carlo replicates saved to the database as one batch,
// with every replicate's ticks, plus a summary CSV at the end. ---
static async Task<int> RunBatchAsync(SimulationConfig config, int replicateCount)
{
    Console.WriteLine($"Running {replicateCount} Monte Carlo replicates...");

    int baseSeed = config.RandomSeed ?? Random.Shared.Next();
    var runner = new MonteCarloRunner();
    var results = await runner.RunAsync(config, replicateCount, baseSeed);

    foreach (var result in results)
    {
        Console.WriteLine($"Replicate {result.ReplicateIndex} (seed={result.Seed}): {result.FinalStatistics}");
    }

    var repository = new SimulationRepository(Path.Combine(AppContext.BaseDirectory, "simulations.db"));
    var created = DateTime.UtcNow;
    var batch = new Batch
    {
        Id = Guid.NewGuid(),
        GroupId = Guid.NewGuid(),
        Model = config.Model.ToString(),
        CreatedAt = created,
        Runs = results.Count,
        BaseSeed = baseSeed,
        TickLimit = config.TickLimit,
        ConfigurationJson = JsonSerializer.Serialize(config),
        SummaryJson = JsonSerializer.Serialize(ComparisonSummary.Build(config.Model, baseSeed, results),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    var runs = results.Select(result =>
    {
        var simulationId = Guid.NewGuid();
        return new BatchRun
        {
            Simulation = new Simulation
            {
                Id = simulationId,
                Model = batch.Model,
                ScenarioName = config.ScenarioName,
                Source = "Console",
                Status = "Completed",
                StartedAt = created,
                EndedAt = created,
                TickCount = result.History.Count,
                Seed = result.Seed,
                ConfigurationJson = batch.ConfigurationJson,
                BatchId = batch.Id,
                RunNumber = result.ReplicateIndex + 1
            },
            Environment = new EnvironmentRecord
            {
                Id = Guid.NewGuid(),
                SimulationId = simulationId,
                GridWidth = config.GridWidth,
                GridHeight = config.GridHeight,
                Topology = config.Topology.ToString(),
                Scheduler = config.Scheduler.ToString()
            },
            Ticks = result.History.Select(stats => new TickRecord
            {
                SimulationId = simulationId,
                TickNumber = stats.Tick,
                Susceptible = stats.Susceptible,
                Infected = stats.Infected,
                Recovered = stats.Recovered,
                TotalPopulation = stats.TotalPopulation,
                Metrics = stats.Metrics
            }).ToList()
        };
    }).ToList();

    await repository.SaveBatchAsync(batch, runs);
    Console.WriteLine();
    Console.WriteLine($"Batch saved to the database: {repository.DatabasePath}");

    var csvPath = Path.Combine(AppContext.BaseDirectory, $"monte-carlo-{config.Model}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    await MonteCarloCsvExporter.ExportAsync(results, csvPath);
    Console.WriteLine($"Batch results exported to: {csvPath}");

    return 0;
}

static ParsedArgs ParseArgs(string[] args)
{
    var result = new ParsedArgs();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--config" when i + 1 < args.Length:
                result.ConfigPath = args[++i];
                break;
            case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var seed):
                result.Seed = seed;
                i++;
                break;
            case "--ticks" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ticks):
                result.Ticks = ticks;
                i++;
                break;
            case "--agents" when i + 1 < args.Length && int.TryParse(args[i + 1], out var agents):
                result.Agents = agents;
                i++;
                break;
            case "--batch" when i + 1 < args.Length && int.TryParse(args[i + 1], out var batch):
                result.BatchReplicates = batch;
                i++;
                break;
        }
    }
    return result;
}

static void PrintSummary(SimulationConfig config)
{
    Console.WriteLine($"Scenario:            {config.ScenarioName}");
    Console.WriteLine($"Model:               {config.Model}");
    Console.WriteLine($"Grid:                {config.GridWidth} x {config.GridHeight} ({config.Topology})");
    Console.WriteLine($"Agents:              {config.AgentCount}");
    Console.WriteLine($"Tick limit:          {config.TickLimit}");
    Console.WriteLine($"Scheduler:           {config.Scheduler}");
    Console.WriteLine($"Random seed:         {(config.RandomSeed?.ToString() ?? "none (non-deterministic)")}");
    Console.WriteLine();
}

internal sealed class ParsedArgs
{
    public string? ConfigPath { get; set; }
    public int? Seed { get; set; }
    public int? Ticks { get; set; }
    public int? Agents { get; set; }
    public int? BatchReplicates { get; set; }
}
