namespace AbmFramework.Engine;

public sealed class TickStatistics
{
    //Current simulation tick.
    public int Tick { get; init; }

    //Number of susceptible agents. Only meaningful for the SIR model;
    //0 for every other model.
    public int Susceptible { get; init; }

    //Number of infected agents. Only meaningful for the SIR model;
    //0 for every other model.
    public int Infected { get; init; }

    //Number of recovered agents. Only meaningful for the SIR model;
    //0 for every other model.
    public int Recovered { get; init; }

    //Total number of agents in the simulation.
    public int TotalPopulation { get; init; }

    //Generic per-model metrics (e.g. "HappyAgents" for Schelling,
    //"FoodCollected" for Ant Foraging). Empty for SIR, which uses the
    //dedicated fields above for backward compatibility with the existing
    //dashboard chart and persistence schema.
    public IReadOnlyDictionary<string, double> Metrics { get; init; } =
        new Dictionary<string, double>();

    //Time the statistics were generated.
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    //Returns a readable representation of the statistics.
    public override string ToString()
    {
        if (Metrics.Count == 0)
        {
            return $"Tick {Tick} | S={Susceptible} I={Infected} R={Recovered}";
        }

        var metricsText = string.Join(" ", Metrics.Select(m => $"{m.Key}={m.Value:F1}"));
        return $"Tick {Tick} | {metricsText}";
    }
}
