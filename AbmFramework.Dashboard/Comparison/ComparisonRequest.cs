using System.Text.Json;

namespace AbmFramework.Dashboard.Comparison;

// What the Compare panel sends to run one batch of runs.
public class ComparisonRequest
{
    // ModelType name, e.g. "SIR" or "WolfSheep".
    public string Model { get; set; } = "";

    // How many times to run the model (2 to 100).
    public int Runs { get; set; }

    // Optional. The same base seed and settings give the same comparison.
    public int? Seed { get; set; }

    // "A" or "B" when two settings are compared side by side. Only used to
    // label progress messages.
    public string Label { get; set; } = "";

    // Settings that differ from the model's defaults, keyed by
    // SimulationConfig property name (e.g. "infectionProbability": 0.2).
    // Using the property names means new sliders need no server change.
    public Dictionary<string, JsonElement> Settings { get; set; } = new();
}

public class ComparisonProgress
{
    public string Label { get; set; } = "";
    public int Completed { get; set; }
    public int Total { get; set; }
}

// Thrown for a request the user can fix (bad grid size, too many agents...).
// The hub passes the message on to the page, which shows it in red.
public sealed class ComparisonRequestException : Exception
{
    public ComparisonRequestException(string message) : base(message)
    {
    }
}
