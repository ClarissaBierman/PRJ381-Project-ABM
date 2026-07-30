using AbmFramework.Config;
using AbmFramework.Engine;

var options = ParseArgs(args);

if (options.ConfigPath is null)
{
    Console.Error.WriteLine("Missing required flag: --config <path-to-scenario-file>");
    Console.Error.WriteLine("Example: dotnet run -- --config ../configs/sir-default.json");
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

ISimulationEngine engine = new SimulationEngine();

engine.TickCompleted += (sender, stats) =>
{
    Console.WriteLine($"Tick {stats.Tick} | S={stats.Susceptible} I={stats.Infected} R={stats.Recovered}");
};

engine.SimulationCompleted += (sender, e) =>
{
    Console.WriteLine();
    Console.WriteLine("Simulation completed.");
};

await engine.StartAsync(config);

return 0;

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
        }
    }
    return result;
}

static void PrintSummary(SimulationConfig config)
{
    Console.WriteLine($"Scenario:            {config.ScenarioName}");
    Console.WriteLine($"Grid:                {config.GridWidth} x {config.GridHeight} ({config.Topology})");
    Console.WriteLine($"Agents:              {config.AgentCount} (initial infected: {config.InitialInfected})");
    Console.WriteLine($"Infection prob.:     {config.InfectionProbability}");
    Console.WriteLine($"Recovery ticks:      {config.RecoveryTicks}");
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
}

