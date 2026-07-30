namespace AbmFramework.Engine;

public sealed class TickStatistics
{
    //Current simulation tick.
    public int Tick { get; init; }

    //Number of susceptible agents.
    public int Susceptible { get; init; }

    //Number of infected agents.
    public int Infected { get; init; }

    //Number of recovered agents.
    public int Recovered { get; init; }

    //Total number of agents in the simulation.
    public int TotalPopulation =>
        Susceptible + Infected + Recovered;

    //Time the statistics were generated.
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    //Returns a readable representation of the statistics.
    public override string ToString()
    {
        return $"Tick {Tick} | " +
               $"S={Susceptible} " +
               $"I={Infected} " +
               $"R={Recovered}";
    }
}