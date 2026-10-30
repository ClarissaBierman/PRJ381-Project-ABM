// =============================================================================
// TickData.cs  (AbmFramework.Dashboard.Models)
//
// One tick's numbers as sent to the page with the "ReceiveTick" message, for
// the monitors and the live chart. The web version of TickStatistics.
// =============================================================================

namespace AbmFramework.Dashboard.Models;

/// <summary>One tick's statistics, for the page's monitors and chart.</summary>
public class TickData
{
    /// <summary>Which run this belongs to. The page ignores ticks from an old run.</summary>
    public int RunId { get; set; }

    /// <summary>The tick number.</summary>
    public int Tick { get; set; }

    /// <summary>SIR only.</summary>
    public double Susceptible { get; set; }

    /// <summary>SIR only.</summary>
    public double Infected { get; set; }

    /// <summary>SIR only.</summary>
    public double Recovered { get; set; }

    /// <summary>Every other model's numbers by name, e.g. "Sheep".</summary>
    public IReadOnlyDictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();
}
