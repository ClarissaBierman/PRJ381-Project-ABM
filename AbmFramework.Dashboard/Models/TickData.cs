namespace AbmFramework.Dashboard.Models;

public class TickData
{
    public int RunId { get; set; }
    public int Tick { get; set; }
    public double Susceptible { get; set; }
    public double Infected { get; set; }
    public double Recovered { get; set; }
    public IReadOnlyDictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();
}
