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

        if (config.GridWidth > 0 && config.GridHeight > 0 &&
            config.AgentCount > config.GridWidth * config.GridHeight)
        {
            errors.Add(
                $"AgentCount ({config.AgentCount}) exceeds available grid cells " +
                $"({config.GridWidth}x{config.GridHeight} = {config.GridWidth * config.GridHeight}).");
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

        if (string.IsNullOrWhiteSpace(config.ScenarioName))
            errors.Add("ScenarioName cannot be empty.");

        if (errors.Count > 0)
        {
            throw new ConfigValidationException(errors);
        }
    }
}
