using ABM.Core;
using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Engine.Tests;

// How the engine sets up, runs and measures each of the five models.
public class EngineModelTests
{
    private static async Task<(SimulationEngine Engine, IReadOnlyList<TickStatistics> History)> Run(SimulationConfig config)
    {
        var engine = new SimulationEngine();
        var history = await engine.RunToCompletionAsync(config);
        return (engine, history);
    }

    private static void AssertDistinctPositions(IEnumerable<Agent> agents)
    {
        var positions = agents.Select(a => a.Position).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    // --- SIR ---

    [Fact]
    public async Task SIR_StartsWithInitialInfectedAndEveryoneElseSusceptible()
    {
        var config = TestConfigs.For(ModelType.SIR, tickLimit: 1);
        config.InfectionProbability = 0.0; // nobody new gets infected in the first tick

        var (_, history) = await Run(config);

        Assert.Equal(config.InitialInfected, history[0].Infected);
        Assert.Equal(config.AgentCount - config.InitialInfected, history[0].Susceptible);
        Assert.Equal(0, history[0].Recovered);
        Assert.Equal(config.AgentCount, history[0].TotalPopulation);
    }

    [Fact]
    public async Task SIR_PopulationIsConservedAndOnlyFlowsSusceptibleToInfectedToRecovered()
    {
        var config = TestConfigs.For(ModelType.SIR, tickLimit: 60);

        var (_, history) = await Run(config);

        Assert.All(history, s => Assert.Equal(config.AgentCount, s.Susceptible + s.Infected + s.Recovered));
        for (int i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].Susceptible <= history[i - 1].Susceptible, $"Susceptible rose at tick {i}");
            Assert.True(history[i].Recovered >= history[i - 1].Recovered, $"Recovered fell at tick {i}");
        }
        Assert.True(history[^1].Recovered > 0, "Expected some agents to recover over 60 ticks.");
    }

    [Fact]
    public async Task SIR_AgentsArePlacedOnDistinctCells()
    {
        var config = TestConfigs.For(ModelType.SIR);

        var (engine, _) = await Run(config);
        var agents = engine.GetSnapshot().Agents;

        Assert.Equal(config.AgentCount, agents.Count);
        Assert.All(agents, a => Assert.IsType<SIRAgent>(a));
        AssertDistinctPositions(agents);
    }

    // --- Schelling ---

    [Fact]
    public async Task Schelling_SplitsAgentsIntoGroupsByGroupARatio()
    {
        var config = TestConfigs.For(ModelType.Schelling, tickLimit: 1); // 100 agents, 40% group A

        var (engine, _) = await Run(config);
        var agents = engine.GetSnapshot().Agents.Cast<SchellingAgent>().ToList();

        Assert.Equal(100, agents.Count);
        Assert.Equal(40, agents.Count(a => a.Group == 0));
        Assert.Equal(60, agents.Count(a => a.Group == 1));
    }

    [Fact]
    public async Task Schelling_RelocationNeverPutsTwoAgentsOnOneCell()
    {
        var (engine, history) = await Run(TestConfigs.For(ModelType.Schelling, tickLimit: 40));

        AssertDistinctPositions(engine.GetSnapshot().Agents);
        Assert.All(history, s =>
            Assert.Equal(s.TotalPopulation, s.Metrics["HappyAgents"] + s.Metrics["UnhappyAgents"]));
    }

    [Fact]
    public async Task Schelling_UnhappyAgentsMoving_MakesMoreAgentsHappy()
    {
        var (_, history) = await Run(TestConfigs.For(ModelType.Schelling, tickLimit: 40));

        Assert.True(history[^1].Metrics["HappyFraction"] > history[0].Metrics["HappyFraction"],
            $"Happy fraction went from {history[0].Metrics["HappyFraction"]:F2} to {history[^1].Metrics["HappyFraction"]:F2}");
    }

    // --- Boids ---

    [Fact]
    public async Task Boids_KeepPopulationRespectMaxSpeedAndStayOnTheGrid()
    {
        var config = TestConfigs.For(ModelType.Boids, tickLimit: 60);

        var (engine, history) = await Run(config);

        Assert.All(history, s =>
        {
            Assert.Equal(config.AgentCount, s.TotalPopulation);
            Assert.True(s.Metrics["AverageSpeed"] <= config.MaxSpeed + 1e-9);
        });
        Assert.All(engine.GetSnapshot().Agents, a =>
        {
            Assert.InRange(a.Position.X, 0, config.GridWidth - 1);
            Assert.InRange(a.Position.Y, 0, config.GridHeight - 1);
        });
    }

    // --- Ant Foraging ---

    [Fact]
    public async Task AntForaging_PlacesExactlyTheConfiguredFoodAndStartsAntsAtTheNest()
    {
        var config = TestConfigs.For(ModelType.AntForaging, tickLimit: 1);
        var nest = (X: config.GridWidth / 2, Y: config.GridHeight / 2);

        var (engine, history) = await Run(config);

        // Ants leave the nest on their first step, so no food is picked up yet.
        Assert.Equal(config.FoodSources * config.FoodPerSource, history[0].Metrics["FoodRemaining"]);
        Assert.Equal(0, history[0].Metrics["FoodCollected"]);
        Assert.All(engine.GetSnapshot().Agents, a =>
            Assert.True(Math.Max(Math.Abs(a.Position.X - nest.X), Math.Abs(a.Position.Y - nest.Y)) <= 1));
    }

    [Fact]
    public async Task AntForaging_FoodOnlyEverDecreasesAndAntsCollectSome()
    {
        var (_, history) = await Run(TestConfigs.For(ModelType.AntForaging, tickLimit: 400));

        for (int i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].Metrics["FoodRemaining"] <= history[i - 1].Metrics["FoodRemaining"],
                $"Food increased at tick {i}");
        }
        Assert.True(history[^1].Metrics["FoodCollected"] > 0, "Expected ants to find some food in 400 ticks.");
    }

    [Fact]
    public async Task AntForaging_FullDecayRate_ClearsAllPheromoneEveryTick()
    {
        var config = TestConfigs.For(ModelType.AntForaging, tickLimit: 400);
        config.PheromoneDecayRate = 1.0;

        var (engine, history) = await Run(config);

        Assert.True(history[^1].Metrics["FoodCollected"] > 0, "Test needs ants to have laid some pheromone.");
        Assert.All(engine.GetSnapshot().Grid.AllPatches(), p =>
            Assert.Equal(0.0, p.GetProperty("pheromone") is double v ? v : 0.0));
    }

    // --- Wolf-Sheep ---

    [Fact]
    public async Task WolfSheep_CountsMatchTheLivingAgentsAndIdsStayUnique()
    {
        var config = TestConfigs.For(ModelType.WolfSheep, tickLimit: 40);

        var (engine, history) = await Run(config);
        var snapshot = engine.GetSnapshot();
        var agents = snapshot.Agents;

        Assert.All(agents, a => Assert.True(a.IsAlive, $"Dead agent {a.Id} was left in the simulation."));
        Assert.Equal(agents.Count, agents.Select(a => a.Id).Distinct().Count());
        Assert.Equal(agents.OfType<SheepAgent>().Count(), (int)snapshot.Statistics.Metrics["Sheep"]);
        Assert.Equal(agents.OfType<WolfAgent>().Count(), (int)snapshot.Statistics.Metrics["Wolves"]);
        Assert.All(history, s =>
        {
            Assert.Equal(s.TotalPopulation, s.Metrics["Sheep"] + s.Metrics["Wolves"]);
            Assert.InRange(s.Metrics["Grass"], 0, config.GridWidth * config.GridHeight);
        });
    }

    [Fact]
    public async Task WolfSheep_WellFedSheepWithoutWolves_Multiply()
    {
        var config = TestConfigs.For(ModelType.WolfSheep, tickLimit: 30);
        config.InitialWolves = 0;
        config.InitialSheepEnergy = 1000;
        config.SheepReproductionProbability = 0.2;

        var (_, history) = await Run(config);

        Assert.All(history, s => Assert.Equal(0, s.Metrics["Wolves"]));
        Assert.True(history[^1].Metrics["Sheep"] > config.InitialSheep, "Expected the flock to grow.");
    }

    [Fact]
    public async Task WolfSheep_StarvingWolvesWithoutPrey_AllDie()
    {
        var config = TestConfigs.For(ModelType.WolfSheep, tickLimit: 30);
        config.InitialSheep = 0;
        config.InitialWolfEnergy = 5;
        config.EnergyLossPerTick = 1;
        config.WolfReproductionProbability = 0;

        var (_, history) = await Run(config);

        Assert.Equal(0, history[^1].Metrics["Wolves"]);
        Assert.Equal(0, history[^1].TotalPopulation);
    }

    [Fact]
    public async Task WolfSheep_CertainRegrowth_KeepsEveryPatchGrassy()
    {
        var config = TestConfigs.For(ModelType.WolfSheep, tickLimit: 20);
        config.GrassRegrowthProbability = 1.0;

        var (_, history) = await Run(config);

        Assert.All(history, s => Assert.Equal(config.GridWidth * config.GridHeight, s.Metrics["Grass"]));
    }
}
