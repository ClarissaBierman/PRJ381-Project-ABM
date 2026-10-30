// =============================================================================
// AgentDTO.cs  (AbmFramework.Dashboard.Models)
//
// One agent as sent to the browser to draw. DTO = "data transfer object": a
// small, plain class that only exists to be turned into JSON. The JSON names
// are camelCase (id, x, y, state) because SignalR uses web JSON settings.
// =============================================================================

using System.Text.Json.Serialization;

namespace AbmFramework.Dashboard.Models;

/// <summary>One agent, as the page needs it to draw and inspect.</summary>
public class AgentDTO
{
    /// <summary>The agent's id.</summary>
    public int Id { get; set; }

    /// <summary>Grid column.</summary>
    public int X { get; set; }

    /// <summary>Grid row.</summary>
    public int Y { get; set; }

    /// <summary>The agent's DisplayState. The page picks the icon and colour from this.</summary>
    public string State { get; set; } = "";

    /// <summary>Wolf-Sheep only. Left out of the message for agents without energy.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Energy { get; set; }

    /// <summary>Boids only: direction of travel in radians (0 = right, clockwise on screen).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Heading { get; set; }

    /// <summary>Boids only: cells moved per tick.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Speed { get; set; }
}
