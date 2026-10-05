using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Config.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadFromJson_FullyPopulatedFile_MapsAllFields()
    {
        const string json = """
        {
          "ScenarioName": "Fast Spread",
          "GridWidth": 30,
          "GridHeight": 20,
          "Topology": "Toroidal",
          "AgentCount": 100,
          "InitialInfected": 10,
          "InfectionProbability": 0.6,
          "RecoveryTicks": 5,
          "TickLimit": 300,
          "RandomSeed": 42,
          "Scheduler": "Sequential"
        }
        """;

        var config = ConfigLoader.LoadFromJson(json);

        Assert.Equal("Fast Spread", config.ScenarioName);
        Assert.Equal(30, config.GridWidth);
        Assert.Equal(20, config.GridHeight);
        Assert.Equal(GridTopology.Toroidal, config.Topology);
        Assert.Equal(100, config.AgentCount);
        Assert.Equal(10, config.InitialInfected);
        Assert.Equal(0.6, config.InfectionProbability);
        Assert.Equal(5, config.RecoveryTicks);
        Assert.Equal(300, config.TickLimit);
        Assert.Equal(42, config.RandomSeed);
        Assert.Equal(SchedulerKind.Sequential, config.Scheduler);
    }

    [Fact]
    public void LoadFromJson_PartialFile_FillsRemainingFieldsWithDefaults()
    {
        const string json = """{ "AgentCount": 50 }""";

        var config = ConfigLoader.LoadFromJson(json);

        Assert.Equal(50, config.AgentCount);
        Assert.Equal(50, config.GridWidth);
        Assert.Equal(0.3, config.InfectionProbability);
    }

    [Fact]
    public void LoadFromJson_MalformedJson_ThrowsConfigValidationException()
    {
        const string brokenJson = "{ \"AgentCount\": ";

        Assert.Throws<ConfigValidationException>(() => ConfigLoader.LoadFromJson(brokenJson));
    }

    [Fact]
    public void LoadFromYaml_FullyPopulatedFile_MapsAllFields()
    {
        const string yaml = """
        ScenarioName: Slow Burn
        GridWidth: 40
        GridHeight: 40
        AgentCount: 150
        InitialInfected: 3
        InfectionProbability: 0.15
        RecoveryTicks: 14
        TickLimit: 1000
        RandomSeed: 7
        """;

        var config = ConfigLoader.LoadFromYaml(yaml);

        Assert.Equal("Slow Burn", config.ScenarioName);
        Assert.Equal(40, config.GridWidth);
        Assert.Equal(150, config.AgentCount);
        Assert.Equal(3, config.InitialInfected);
        Assert.Equal(0.15, config.InfectionProbability);
        Assert.Equal(14, config.RecoveryTicks);
        Assert.Equal(1000, config.TickLimit);
        Assert.Equal(7, config.RandomSeed);
    }

    [Theory]
    [InlineData(0, 50, 200, 5, 0.3, 10, 500)]
    [InlineData(50, 0, 200, 5, 0.3, 10, 500)]
    [InlineData(50, 50, 0, 5, 0.3, 10, 500)]
    [InlineData(50, 50, 200, -1, 0.3, 10, 500)]
    [InlineData(50, 50, 200, 5, 1.5, 10, 500)]
    [InlineData(50, 50, 200, 5, 0.3, 0, 500)]
    [InlineData(50, 50, 200, 5, 0.3, 10, 0)]
    public void Validate_InvalidValues_ThrowsWithAllErrorsListed(
        int width, int height, int agents, int initialInfected,
        double infectionProb, int recoveryTicks, int tickLimit)
    {
        var config = new SimulationConfig
        {
            GridWidth = width,
            GridHeight = height,
            AgentCount = agents,
            InitialInfected = initialInfected,
            InfectionProbability = infectionProb,
            RecoveryTicks = recoveryTicks,
            TickLimit = tickLimit
        };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public void Validate_InitialInfectedExceedsAgentCount_Throws()
    {
        var config = new SimulationConfig { AgentCount = 10, InitialInfected = 20 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("InitialInfected"));
    }

    [Fact]
    public void Validate_AgentCountExceedsGridCapacity_Throws()
    {
        var config = new SimulationConfig { GridWidth = 5, GridHeight = 5, AgentCount = 100, InitialInfected = 1 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("exceeds available grid cells"));
    }

    [Theory]
    [InlineData(201, 50, "GridWidth")]
    [InlineData(50, 201, "GridHeight")]
    public void Validate_GridLargerThanMaximum_Throws(int width, int height, string field)
    {
        var config = new SimulationConfig { GridWidth = width, GridHeight = height };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains(field) && e.Contains("at most"));
    }

    [Fact]
    public void Validate_GridAtMaximumSize_IsValid()
    {
        var config = new SimulationConfig { GridWidth = ConfigLoader.MaxGridSize, GridHeight = ConfigLoader.MaxGridSize };

        Assert.Null(Record.Exception(() => ConfigLoader.Validate(config)));
    }

    [Fact]
    public void Validate_DefaultConfig_IsValid()
    {
        var config = new SimulationConfig();

        var exception = Record.Exception(() => ConfigLoader.Validate(config));

        Assert.Null(exception);
    }

    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => ConfigLoader.LoadFromFile("does-not-exist.json"));
    }

    [Fact]
    public void LoadFromJson_ModelDefaultsToSIR_WhenNotSpecified()
    {
        const string json = """{ "AgentCount": 50 }""";

        var config = ConfigLoader.LoadFromJson(json);

        Assert.Equal(ModelType.SIR, config.Model);
    }

    [Theory]
    [InlineData("Schelling")]
    [InlineData("Boids")]
    [InlineData("AntForaging")]
    public void LoadFromJson_EachModelType_Parses(string modelName)
    {
        var json = $$"""{ "Model": "{{modelName}}", "AgentCount": 50 }""";

        var config = ConfigLoader.LoadFromJson(json);

        Assert.Equal(modelName, config.Model.ToString());
    }

    [Fact]
    public void Validate_SchellingInvalidSimilarityThreshold_Throws()
    {
        var config = new SimulationConfig { Model = ModelType.Schelling, SimilarityThreshold = 1.5 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("SimilarityThreshold"));
    }

    [Fact]
    public void Validate_BoidsNonPositiveMaxSpeed_Throws()
    {
        var config = new SimulationConfig { Model = ModelType.Boids, MaxSpeed = 0 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("MaxSpeed"));
    }

    [Fact]
    public void Validate_AntForagingInvalidDecayRate_Throws()
    {
        var config = new SimulationConfig { Model = ModelType.AntForaging, PheromoneDecayRate = 2.0 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("PheromoneDecayRate"));
    }

    [Fact]
    public void Validate_DefaultConfigForEveryModel_IsValid()
    {
        foreach (var model in Enum.GetValues<ModelType>())
        {
            var config = new SimulationConfig { Model = model };

            var exception = Record.Exception(() => ConfigLoader.Validate(config));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void Validate_InitialInfectedCannotExceedPopulation()
    {
        var config = new SimulationConfig { Model = ModelType.SIR, AgentCount = 10, InitialInfected = 11 };

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));

        Assert.Contains(exception.Errors, error => error.Contains("cannot exceed AgentCount"));
    }

    [Fact]
    public void Validate_TickLimitMustBePositive()
    {
        var config = new SimulationConfig { TickLimit = 0 };

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));

        Assert.Contains(exception.Errors, error => error.Contains("TickLimit"));
    }

    [Theory]
    [InlineData(ModelType.SIR)]
    [InlineData(ModelType.Schelling)]
    [InlineData(ModelType.Boids)]
    [InlineData(ModelType.AntForaging)]
    public void Validate_MoreAgentsThanCells_ThrowsForEveryModel(ModelType model)
    {
        var config = new SimulationConfig { Model = model, GridWidth = 5, GridHeight = 5, AgentCount = 1000, InitialInfected = 1 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(config));
        Assert.Contains(ex.Errors, e => e.Contains("exceeds available grid cells"));
    }

    [Fact]
    public void Validate_WolfSheep_CountsSheepAndWolvesAgainstTheGrid()
    {
        var tooMany = new SimulationConfig { Model = ModelType.WolfSheep, GridWidth = 5, GridHeight = 5, AgentCount = 25, InitialSheep = 20, InitialWolves = 10 };
        var fits = new SimulationConfig { Model = ModelType.WolfSheep, GridWidth = 5, GridHeight = 5, AgentCount = 25, InitialSheep = 20, InitialWolves = 5 };

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Validate(tooMany));
        Assert.Contains(ex.Errors, e => e.Contains("Sheep plus wolves (30)"));
        Assert.Null(Record.Exception(() => ConfigLoader.Validate(fits)));
    }

    [Fact]
    public void Validate_AgentsFillingEveryCell_IsAllowed()
    {
        var config = new SimulationConfig { Model = ModelType.Boids, GridWidth = 5, GridHeight = 5, AgentCount = 25 };

        Assert.Null(Record.Exception(() => ConfigLoader.Validate(config)));
    }

    [Fact]
    public void LoadFromFile_UnsupportedExtension_ThrowsNotSupportedException()
    {
        var tempPath = Path.GetTempFileName();
        var renamed = Path.ChangeExtension(tempPath, ".txt");
        File.Move(tempPath, renamed, overwrite: true);
        File.WriteAllText(renamed, "irrelevant");

        try
        {
            Assert.Throws<NotSupportedException>(() => ConfigLoader.LoadFromFile(renamed));
        }
        finally
        {
            File.Delete(renamed);
        }
    }
}
