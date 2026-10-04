using System.Text.Json.Serialization;

namespace AbmFramework.Dashboard.Models;

public class AgentDTO
{
    public int Id { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public string State { get; set; } = "";

    // Wolf-Sheep only; left out of the message for agents without energy.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Energy { get; set; }
}
