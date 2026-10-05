using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbmFramework.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static SimulationConfig LoadAndValidate(string filePath)
    {
        var config = LoadFromFile(filePath);
        Validate(config);
        return config;
    }

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

    public static void Validate(SimulationConfig config)
    {
        var errors = new List<string>();

        if (config.GridWidth <= 0)
            errors.Add($"GridWidth must be positive (got {config.GridWidth}).");

        if (config.GridHeight <= 0)
            errors.Add($"GridHeight must be positive (got {config.GridHeight}).");

        if (config.AgentCount <= 0)
            errors.Add($"AgentCount must be positive (got {config.AgentCount}).");

        if (config.Model is ModelType.SIR or ModelType.Schelling &&
            config.GridWidth > 0 && config.GridHeight > 0 &&
            config.AgentCount > (long)config.GridWidth * config.GridHeight)
        {
            errors.Add(
                $"AgentCount ({config.AgentCount}) exceeds available grid cells " +
                $"({config.GridWidth}x{config.GridHeight} = {(long)config.GridWidth * config.GridHeight}).");
        }

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

        if (config.TickLimit <= 0)
            errors.Add($"TickLimit must be positive (got {config.TickLimit}).");
        else if (config.TickLimit > 1_000_000)
            errors.Add($"TickLimit cannot exceed 1,000,000 (got {config.TickLimit}).");

        if (string.IsNullOrWhiteSpace(config.ScenarioName))
            errors.Add("ScenarioName cannot be empty.");

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
