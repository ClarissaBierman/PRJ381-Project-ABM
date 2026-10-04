namespace AbmFramework.Dashboard.Models;

public class GridData
{
    public int RunId { get; set; }
    public List<AgentDTO> Agents { get; set; } = new();
    public List<PatchDTO> GrassPatches { get; set; } = new();
}
