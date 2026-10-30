// =============================================================================
// ModelOption.cs  (AbmFramework.Dashboard.Models)
//
// One entry of the model drop-down. Built by ModelCatalog.
// =============================================================================

namespace AbmFramework.Dashboard.Models;

/// <summary>One entry of the model drop-down.</summary>
public class ModelOption
{
    /// <summary>The ModelType name the server understands, e.g. "WolfSheep".</summary>
    public string Model { get; set; } = "";

    /// <summary>The name shown to the user, e.g. "Wolf-Sheep Predation".</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>One sentence explaining the model.</summary>
    public string Description { get; set; } = "";
}
