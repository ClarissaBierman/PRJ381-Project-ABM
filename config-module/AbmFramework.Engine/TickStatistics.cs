// =============================================================================
// TickStatistics.cs  (AbmFramework.Engine)
//
// The numbers counted at the end of each tick. This is what feeds the live
// chart and monitors, the database (one row per tick), the Excel/CSV exports
// and the Compare results.
//
// SIR has its own fields (Susceptible, Infected, Recovered) because it was the
// first model and the chart and database were built around it. Every other
// model puts its numbers in the Metrics dictionary, for example
// Metrics["Sheep"]. A new model should use Metrics too.
// =============================================================================

namespace AbmFramework.Engine;

/// <summary>The statistics for one tick of a simulation.</summary>
public sealed class TickStatistics
{
    /// <summary>The tick these numbers belong to (0 is the first tick).</summary>
    public int Tick { get; init; }

    /// <summary>Number of susceptible agents. SIR only, 0 for every other model.</summary>
    public int Susceptible { get; init; }

    /// <summary>Number of infected agents. SIR only, 0 for every other model.</summary>
    public int Infected { get; init; }

    /// <summary>Number of recovered agents. SIR only, 0 for every other model.</summary>
    public int Recovered { get; init; }

    /// <summary>Total number of living agents.</summary>
    public int TotalPopulation { get; init; }

    /// <summary>
    /// Per-model numbers by name, for example "HappyAgents" for Schelling or
    /// "FoodCollected" for Ant Foraging. Empty for SIR, which uses the
    /// dedicated fields above for backward compatibility with the existing
    /// dashboard chart and database tables.
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics { get; init; } =
        new Dictionary<string, double>();

    /// <summary>When the statistics were calculated (UTC).</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>A short readable summary, for console output and debugging.</summary>
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
