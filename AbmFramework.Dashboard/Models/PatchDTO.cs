using System.Text.Json.Serialization;

namespace AbmFramework.Dashboard.Models;

// One coloured grid cell. Only the fields a model actually uses are sent,
// so an Ant Foraging patch never carries a Grass value and vice versa.
public class PatchDTO
{
    public int X { get; set; }

    public int Y { get; set; }

    // Wolf-Sheep: only eaten (false) patches are sent, see GridData.HasGrass.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Grass { get; set; }

    // Ant Foraging: food left on this cell.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Food { get; set; }

    // Ant Foraging: pheromone level, fades over time.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Pheromone { get; set; }

    // Ant Foraging: the ants' home cell.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Nest { get; set; }
}
