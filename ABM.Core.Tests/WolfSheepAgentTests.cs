using Xunit;

namespace ABM.Core.Tests;

public class SheepAgentTests
{
    // A roll of 0.999 fails any reproduction check below 1; 0.0 passes it.
    private static readonly Random NoReproduce = new FixedRandom(0.999);
    private static readonly Random AlwaysReproduce = new FixedRandom(0.0);

    private static SheepAgent Sheep(double energy = 5, double reproductionProbability = 0.0) =>
        new(0, (5, 5), energy, energyLossPerTick: 1, grassEnergyGain: 4, reproductionProbability);

    [Fact]
    public void OnGrass_EatsItAndGainsEnergy()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(5, 5, "grass", true);
        var sheep = Sheep(energy: 5);

        sheep.Step(Array.Empty<Agent>(), NoReproduce, grid);

        Assert.Equal(8, sheep.Energy); // 5 - 1 + 4
        Assert.Equal(false, grid.GetPatch(5, 5).GetProperty("grass"));
    }

    [Fact]
    public void WithoutGrass_LosesEnergy()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var sheep = Sheep(energy: 5);

        sheep.Step(Array.Empty<Agent>(), NoReproduce, grid);

        Assert.Equal(4, sheep.Energy);
        Assert.True(sheep.IsAlive);
    }

    [Fact]
    public void RunningOutOfEnergy_DiesWithoutMovingOrReproducing()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var sheep = Sheep(energy: 1, reproductionProbability: 1.0);

        sheep.Step(Array.Empty<Agent>(), AlwaysReproduce, grid);

        Assert.False(sheep.IsAlive);
        Assert.Equal((5, 5), sheep.Position);
        Assert.Empty(sheep.CreatePendingOffspring(() => 99));
    }

    [Fact]
    public void Reproducing_HalvesEnergyAndQueuesOneLamb()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var sheep = Sheep(energy: 11, reproductionProbability: 1.0);

        sheep.Step(Array.Empty<Agent>(), AlwaysReproduce, grid);
        var offspring = sheep.CreatePendingOffspring(() => 42);

        Assert.Equal(5, sheep.Energy); // (11 - 1) / 2
        var lamb = Assert.IsType<SheepAgent>(Assert.Single(offspring));
        Assert.Equal(42, lamb.Id);
        Assert.Equal(5, lamb.Energy);
        Assert.Equal((5, 5), lamb.Position); // born where the parent stood
        Assert.Empty(sheep.CreatePendingOffspring(() => 43)); // queue is cleared
    }

    [Fact]
    public void Step_MovesOneCellUpDownLeftOrRight()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var sheep = Sheep(energy: 1000);
        var rng = new Random(1);

        for (int i = 0; i < 20; i++)
        {
            var before = sheep.Position;
            sheep.Step(Array.Empty<Agent>(), rng, grid);
            Assert.Equal(1, Math.Abs(sheep.Position.X - before.X) + Math.Abs(sheep.Position.Y - before.Y));
        }
    }

    [Fact]
    public void Bounded_SheepInCornerNeverLeavesTheGrid()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var sheep = new SheepAgent(0, (0, 0), 1000, 1, 4, 0.0);
        var rng = new Random(1);

        for (int i = 0; i < 300; i++)
        {
            sheep.Step(Array.Empty<Agent>(), rng, grid);
            Assert.InRange(sheep.Position.X, 0, 9);
            Assert.InRange(sheep.Position.Y, 0, 9);
        }
    }
}

public class WolfAgentTests
{
    private static readonly Random NoReproduce = new FixedRandom(0.999);
    private static readonly Random AlwaysReproduce = new FixedRandom(0.0);

    private static WolfAgent Wolf(double energy = 5, double reproductionProbability = 0.0) =>
        new(0, (5, 5), energy, energyLossPerTick: 1, sheepEnergyGain: 8, reproductionProbability);

    private static SheepAgent Sheep(int id, (int X, int Y) position) =>
        new(id, position, 5, 1, 4, 0.0);

    [Fact]
    public void WithNeighbouringSheep_EatsItAndGainsEnergy()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf(energy: 5);
        var sheep = Sheep(1, (5, 6));

        wolf.Step(new Agent[] { sheep }, NoReproduce, grid);

        Assert.False(sheep.IsAlive);
        Assert.Equal(12, wolf.Energy); // 5 - 1 + 8
    }

    [Fact]
    public void WithSheepOnSameCell_EatsIt()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf();
        var sheep = Sheep(1, (5, 5));
        grid.Place(sheep);

        wolf.Step(Array.Empty<Agent>(), NoReproduce, grid);

        Assert.False(sheep.IsAlive);
    }

    [Fact]
    public void WithSeveralSheep_EatsOnlyOnePerTick()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf();
        var flock = new[] { Sheep(1, (5, 6)), Sheep(2, (4, 5)), Sheep(3, (6, 6)) };

        wolf.Step(flock, NoReproduce, grid);

        Assert.Equal(1, flock.Count(s => !s.IsAlive));
    }

    [Fact]
    public void IgnoresDeadSheepAndOtherWolves()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf(energy: 5);
        var deadSheep = Sheep(1, (5, 6));
        deadSheep.Die();
        var otherWolf = new WolfAgent(2, (4, 5), 5, 1, 8, 0.0);

        wolf.Step(new Agent[] { deadSheep, otherWolf }, NoReproduce, grid);

        Assert.Equal(4, wolf.Energy); // lost energy, ate nothing
        Assert.True(otherWolf.IsAlive);
    }

    [Fact]
    public void RunningOutOfEnergy_DiesWithoutReproducing()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf(energy: 1, reproductionProbability: 1.0);

        wolf.Step(Array.Empty<Agent>(), AlwaysReproduce, grid);

        Assert.False(wolf.IsAlive);
        Assert.Empty(wolf.CreatePendingOffspring(() => 99));
    }

    [Fact]
    public void Reproducing_HalvesEnergyAndQueuesOnePup()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var wolf = Wolf(energy: 11, reproductionProbability: 1.0);

        wolf.Step(Array.Empty<Agent>(), AlwaysReproduce, grid);
        var offspring = wolf.CreatePendingOffspring(() => 42);

        Assert.Equal(5, wolf.Energy); // (11 - 1) / 2
        var pup = Assert.IsType<WolfAgent>(Assert.Single(offspring));
        Assert.Equal(42, pup.Id);
        Assert.Equal(5, pup.Energy);
    }
}
