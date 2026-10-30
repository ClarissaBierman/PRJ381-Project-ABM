// =============================================================================
// PatchDTO.cs  (AbmFramework.Dashboard.Models)
//
// One coloured grid cell, as sent to the page inside GridData.
// =============================================================================

using System.Text.Json.Serialization;

namespace AbmFramework.Dashboard.Models;

/// <summary>
/// One coloured grid cell. Only the fields a model actually uses are sent
/// (the JsonIgnore attributes leave out empty ones), so an Ant Foraging
/// patch never carries a Grass value and vice versa.
/// </summary>
public class PatchDTO
{
    /// <summary>Grid column.</summary>
    public int X { get; set; }

    /// <summary>Grid row.</summary>
    public int Y { get; set; }

    /// <summary>Wolf-Sheep: only eaten (false) patches are sent, see GridData.HasGrass.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Grass { get; set; }

    /// <summary>Ant Foraging: food left on this cell.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Food { get; set; }

    /// <summary>Ant Foraging: pheromone level, fades over time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Pheromone { get; set; }

    /// <summary>Ant Foraging: the ants' home cell.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Nest { get; set; }
}
