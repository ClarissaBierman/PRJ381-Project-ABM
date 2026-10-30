// =============================================================================
// ConfigLoader.cs  (AbmFramework.Config)
//
// Two jobs:
//   1. Load a SimulationConfig from a scenario file (.json, .yaml or .yml).
//      Example files live in config-module/configs.
//   2. Validate a config: check every setting is in a sensible range, and
//      collect ALL the problems before throwing, so the user can fix them in
//      one go instead of one error at a time.
//
// The dashboard doesn't load files, but it does call Validate on the config it
// builds from the sliders, so the same rules protect both paths.
//
// ADD A MODEL: add an "if (config.Model == ModelType.YourModel)" block to
// Validate for your model's settings.
// =============================================================================

using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbmFramework.Config;

/// <summary>Loads scenario files and validates configs.</summary>
public static class ConfigLoader
{
    /// <summary>
    /// The biggest grid allowed in either direction. Bigger grids get slow to
    /// draw and save, see docs/KNOWN-ISSUES.md.
    /// </summary>
    public const int MaxGridSize = 200;

    // Forgiving JSON reading: property names in any case, // comments and
    // trailing commas are all allowed, so hand-written files don't break easily.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // YAML keys are written in PascalCase, exactly like the C# property names
    // (GridWidth: 50). Unknown keys are ignored instead of causing an error.
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Loads a scenario file and validates it. The usual way to load a file.</summary>
    /// <param name="filePath">Path to a .json, .yaml or .yml file.</param>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="NotSupportedException">The file extension isn't .json, .yaml or .yml.</exception>
    /// <exception cref="ConfigValidationException">The file is malformed or a setting is out of range.</exception>
    public static SimulationConfig LoadAndValidate(string filePath)
    {
        var config = LoadFromFile(filePath);
        Validate(config);
        return config;
    }

    /// <summary>
    /// Loads a scenario file WITHOUT validating it. Picks JSON or YAML from the
    /// file extension. Call <see cref="Validate"/> before running the result.
    /// </summary>
    /// <param name="filePath">Path to a .json, .yaml or .yml file.</param>
    public static SimulationConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Scenario config file not found: {filePath}", filePath);
        }

        var contents = File.ReadAllText(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".json" => LoadFromJson(contents),
            ".yaml" or ".yml" => LoadFromYaml(contents),
            _ => throw new NotSupportedException(
                $"Unsupported config file extension '{extension}'. Use .json, .yaml, or .yml.")
        };
    }

    /// <summary>Reads a config from JSON text. Missing settings keep their default values.</summary>
    /// <exception cref="ConfigValidationException">The JSON is malformed or empty.</exception>
    public static SimulationConfig LoadFromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SimulationConfig>(json, JsonOptions)
                   ?? throw new ConfigValidationException(new[] { "Config file is empty or parsed to null." });
        }
        catch (JsonException ex)
        {
            throw new ConfigValidationException(new[] { $"Malformed JSON: {ex.Message}" });
        }
    }

    /// <summary>Reads a config from YAML text. Missing settings keep their default values.</summary>
    /// <exception cref="ConfigValidationException">The YAML is malformed or empty.</exception>
    public static SimulationConfig LoadFromYaml(string yaml)
    {
        try
        {
            return YamlDeserializer.Deserialize<SimulationConfig>(yaml)
                   ?? throw new ConfigValidationException(new[] { "Config file is empty or parsed to null." });
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new ConfigValidationException(new[] { $"Malformed YAML: {ex.Message}" });
        }
    }

    /// <summary>
    /// Checks every setting and throws if anything is wrong. General settings
    /// are always checked, model-specific ones only for the config's own Model.
    /// </summary>
    /// <param name="config">The config to check. It is not changed.</param>
    /// <exception cref="ConfigValidationException">Lists every problem found.</exception>
    public static void Validate(SimulationConfig config)
    {
        var errors = new List<string>();

        // ---- General settings ------------------------------------------------

        if (config.GridWidth <= 0)
            errors.Add($"GridWidth must be positive (got {config.GridWidth}).");
        else if (config.GridWidth > MaxGridSize)
            errors.Add($"GridWidth can be at most {MaxGridSize} (got {config.GridWidth}).");

        if (config.GridHeight <= 0)
            errors.Add($"GridHeight must be positive (got {config.GridHeight}).");
        else if (config.GridHeight > MaxGridSize)
            errors.Add($"GridHeight can be at most {MaxGridSize} (got {config.GridHeight}).");

        if (config.AgentCount <= 0)
            errors.Add($"AgentCount must be positive (got {config.AgentCount}).");

        // Every starting agent needs room on the grid. (SIR and Schelling put one
        // agent per cell, and their setup would loop forever without free cells.)
        // long instead of int so a huge grid can't overflow the multiplication.
        long startingAgents = config.Model == ModelType.WolfSheep
            ? (long)config.InitialSheep + config.InitialWolves
            : config.AgentCount;
        string agentsName = config.Model == ModelType.WolfSheep ? "Sheep plus wolves" : "AgentCount";

        if (config.GridWidth > 0 && config.GridHeight > 0 &&
            startingAgents > (long)config.GridWidth * config.GridHeight)
        {
            errors.Add(
                $"{agentsName} ({startingAgents}) exceeds available grid cells " +
                $"({config.GridWidth}x{config.GridHeight} = {(long)config.GridWidth * config.GridHeight}). " +
                "Use a bigger grid or fewer agents.");
        }

        // ---- SIR (checked for every model, harmless for the others) -------------
        if (config.InitialInfected < 0)
            errors.Add($"InitialInfected cannot be negative (got {config.InitialInfected}).");

        if (config.InitialInfected > config.AgentCount)
        {
            errors.Add(
                $"InitialInfected ({config.InitialInfected}) cannot exceed AgentCount ({config.AgentCount}).");
        }

        if (config.InfectionProbability is < 0.0 or > 1.0)
        {
            errors.Add(
                $"InfectionProbability must be between 0.0 and 1.0 (got {config.InfectionProbability}).");
        }

        if (config.RecoveryTicks <= 0)
            errors.Add($"RecoveryTicks must be positive (got {config.RecoveryTicks}).");

        // ---- Run length and name ------------------------------------------------
        if (config.TickLimit <= 0)
            errors.Add($"TickLimit must be positive (got {config.TickLimit}).");
        else if (config.TickLimit > 1_000_000)
            errors.Add($"TickLimit cannot exceed 1,000,000 (got {config.TickLimit}).");

        if (string.IsNullOrWhiteSpace(config.ScenarioName))
            errors.Add("ScenarioName cannot be empty.");

        // ---- Model-specific settings ------------------------------------------
        if (config.Model == ModelType.Schelling)
        {
            if (config.SimilarityThreshold is < 0.0 or > 1.0)
                errors.Add($"SimilarityThreshold must be between 0.0 and 1.0 (got {config.SimilarityThreshold}).");

            if (config.GroupARatio is < 0.0 or > 1.0)
                errors.Add($"GroupARatio must be between 0.0 and 1.0 (got {config.GroupARatio}).");
        }

        if (config.Model == ModelType.Boids)
        {
            if (config.PerceptionRadius <= 0)
                errors.Add($"PerceptionRadius must be positive (got {config.PerceptionRadius}).");

            if (config.MaxSpeed <= 0)
                errors.Add($"MaxSpeed must be positive (got {config.MaxSpeed}).");

            if (config.SeparationWeight < 0 || config.AlignmentWeight < 0 || config.CohesionWeight < 0)
                errors.Add("Boid separation, alignment, and cohesion weights cannot be negative.");
        }

        if (config.Model == ModelType.AntForaging)
        {
            if (config.FoodSources <= 0)
                errors.Add($"FoodSources must be positive (got {config.FoodSources}).");

            if (config.FoodPerSource <= 0)
                errors.Add($"FoodPerSource must be positive (got {config.FoodPerSource}).");

            if (config.PheromoneDepositAmount < 0)
                errors.Add($"PheromoneDepositAmount cannot be negative (got {config.PheromoneDepositAmount}).");

            if (config.PheromoneDecayRate is < 0.0 or > 1.0)
                errors.Add($"PheromoneDecayRate must be between 0.0 and 1.0 (got {config.PheromoneDecayRate}).");

            if (config.ExplorationChance is < 0.0 or > 1.0)
                errors.Add($"ExplorationChance must be between 0.0 and 1.0 (got {config.ExplorationChance}).");
        }

        if (config.Model == ModelType.WolfSheep)
        {
            if (config.InitialSheep < 0)
                errors.Add($"InitialSheep cannot be negative (got {config.InitialSheep}).");

            if (config.InitialWolves < 0)
                errors.Add($"InitialWolves cannot be negative (got {config.InitialWolves}).");

            if (config.InitialSheepEnergy <= 0)
                errors.Add($"InitialSheepEnergy must be positive (got {config.InitialSheepEnergy}).");

            if (config.InitialWolfEnergy <= 0)
                errors.Add($"InitialWolfEnergy must be positive (got {config.InitialWolfEnergy}).");

            if (config.EnergyLossPerTick <= 0)
                errors.Add($"EnergyLossPerTick must be positive (got {config.EnergyLossPerTick}).");

            if (config.GrassEnergyGain <= 0)
                errors.Add($"GrassEnergyGain must be positive (got {config.GrassEnergyGain}).");

            if (config.SheepEnergyGain <= 0)
                errors.Add($"SheepEnergyGain must be positive (got {config.SheepEnergyGain}).");

            if (config.SheepReproductionProbability is < 0.0 or > 1.0)
                errors.Add($"SheepReproductionProbability must be between 0.0 and 1.0 (got {config.SheepReproductionProbability}).");

            if (config.WolfReproductionProbability is < 0.0 or > 1.0)
                errors.Add($"WolfReproductionProbability must be between 0.0 and 1.0 (got {config.WolfReproductionProbability}).");

            if (config.GrassRegrowthProbability is < 0.0 or > 1.0)
                errors.Add($"GrassRegrowthProbability must be between 0.0 and 1.0 (got {config.GrassRegrowthProbability}).");
        }

        if (errors.Count > 0)
        {
            throw new ConfigValidationException(errors);
        }
    }
}
