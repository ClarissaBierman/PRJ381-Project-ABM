// =============================================================================
// ComparisonRequest.cs  (AbmFramework.Dashboard.Comparison)
//
// The messages the Compare panel and the server exchange:
//   ComparisonRequest           page -> server: run one batch (setting A or B)
//   ComparisonProgress          server -> page: "7 of 100 runs done"
//   ComparisonRequestException  a problem the user can fix, shown in red
// =============================================================================

using System.Text.Json;

namespace AbmFramework.Dashboard.Comparison;

/// <summary>What the Compare panel sends to run one batch of runs.</summary>
public class ComparisonRequest
{
    /// <summary>ModelType name, e.g. "SIR" or "WolfSheep".</summary>
    public string Model { get; set; } = "";

    /// <summary>How many times to run the model (2 to 100).</summary>
    public int Runs { get; set; }

    /// <summary>Optional. The same base seed and settings give the same comparison.</summary>
    public int? Seed { get; set; }

    /// <summary>
    /// "A" or "B" (a single batch is "A"). Labels the progress messages and
    /// is saved with the batch.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>Ties setting A and setting B of one comparison together in the database.</summary>
    public string? GroupId { get; set; }

    /// <summary>The slider values as the panel shows them, saved so the panel can be filled in again.</summary>
    public Dictionary<string, JsonElement>? PageSettings { get; set; }

    /// <summary>
    /// Settings that differ from the model's defaults, keyed by
    /// SimulationConfig property name (e.g. "infectionProbability": 0.2).
    /// Using the property names means new sliders need no server change.
    /// </summary>
    public Dictionary<string, JsonElement> Settings { get; set; } = new();
}

/// <summary>Progress of a running batch, sent to the page after every finished run.</summary>
public class ComparisonProgress
{
    /// <summary>"A" or "B": which setting this is about.</summary>
    public string Label { get; set; } = "";

    /// <summary>Runs finished so far.</summary>
    public int Completed { get; set; }

    /// <summary>Runs in the batch.</summary>
    public int Total { get; set; }
}

/// <summary>
/// Thrown for a request the user can fix (bad grid size, too many agents...).
/// The hub passes the message on to the page, which shows it in red.
/// </summary>
public sealed class ComparisonRequestException : Exception
{
    /// <summary>Creates the exception with the message the user will see.</summary>
    public ComparisonRequestException(string message) : base(message)
    {
    }
}
