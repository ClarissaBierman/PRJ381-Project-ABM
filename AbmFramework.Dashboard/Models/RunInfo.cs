namespace AbmFramework.Dashboard.Models;

public class RunInfo
{
    public int RunId { get; set; }
    public string Model { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public int AgentCount { get; set; }
    public int TickLimit { get; set; }
}
