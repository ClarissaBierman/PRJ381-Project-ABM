using Xunit;

namespace ABM.Core.Tests;

public class AntAgentTests
{
    private static int Chebyshev((int X, int Y) a, (int X, int Y) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double Pheromone(Grid2D grid, int x, int y) =>
        grid.GetPatch(x, y).GetProperty("pheromone") is double p ? p : 0.0;

    [Fact]
    public void Searching_OnFood_PicksUpOneUnitAndHeadsHome()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(3, 3, "food", 5);
        var ant = new AntAgent(0, (3, 3), nestPosition: (8, 8));

        ant.Step(Array.Empty<Agent>(), new Random(1), grid);

        Assert.Equal(4, grid.GetPatch(3, 3).GetProperty("food"));
        Assert.True(ant.CarryingFood);
        Assert.Equal(AntState.ReturningToNest, ant.State);
        Assert.Equal((3, 3), ant.Position);
        Assert.Equal("ReturningWithFood", ant.DisplayState);
    }

    [Fact]
    public void Returning_LaysPheromoneAndStepsTowardsNest()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(3, 3, "food", 5);
        var ant = new AntAgent(0, (3, 3), nestPosition: (8, 6), pheromoneDepositAmount: 2.5);
        var rng = new Random(1);
        ant.Step(Array.Empty<Agent>(), rng, grid); // pick up food

        ant.Step(Array.Empty<Agent>(), rng, grid);

        Assert.Equal(2.5, Pheromone(grid, 3, 3));
        Assert.Equal((4, 4), ant.Position);
    }

    [Fact]
    public void Returning_ReachesNestDropsFoodAndSearchesAgain()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(2, 5, "food", 1);
        var nest = (6, 5);
        var ant = new AntAgent(0, (2, 5), nest);
        var rng = new Random(1);
        ant.Step(Array.Empty<Agent>(), rng, grid); // pick up food

        for (int i = 0; i < 4; i++) ant.Step(Array.Empty<Agent>(), rng, grid);
        Assert.Equal(nest, ant.Position);
        Assert.True(ant.CarryingFood);

        ant.Step(Array.Empty<Agent>(), rng, grid);

        Assert.False(ant.CarryingFood);
        Assert.Equal(AntState.Searching, ant.State);
        Assert.Equal("Searching", ant.DisplayState);
    }

    [Fact]
    public void Searching_WithoutFood_MovesToAnAdjacentCell()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var ant = new AntAgent(0, (5, 5), nestPosition: (5, 5));
        var rng = new Random(1);

        for (int i = 0; i < 20; i++)
        {
            var before = ant.Position;
            ant.Step(Array.Empty<Agent>(), rng, grid);
            Assert.Equal(1, Chebyshev(before, ant.Position));
        }
    }

    [Fact]
    public void Searching_FollowsPheromoneLeadingAwayFromNest()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(6, 5, "pheromone", 1_000_000.0);
        var ant = new AntAgent(0, (5, 5), nestPosition: (5, 5), explorationChance: 0.0);

        // A mid-range roll lands inside the trail cell's (huge) weight.
        ant.Step(Array.Empty<Agent>(), new FixedRandom(0.5), grid);

        Assert.Equal((6, 5), ant.Position);
    }

    [Fact]
    public void Searching_IgnoresPheromoneLeadingBackTowardsNest()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        grid.SetPatchProperty(4, 5, "pheromone", 1_000_000.0); // one step closer to the nest
        var ant = new AntAgent(0, (5, 5), nestPosition: (3, 5), explorationChance: 0.0);

        // If the inward trail were counted, this roll would land inside its
        // weight; with every cell weighted 1 it lands on the last candidate.
        ant.Step(Array.Empty<Agent>(), new FixedRandom(0.99), grid);

        Assert.NotEqual((4, 5), ant.Position);
    }

    [Fact]
    public void Bounded_AntInCornerNeverLeavesTheGrid()
    {
        var grid = new Grid2D(10, 10, GridTopology.Bounded);
        var ant = new AntAgent(0, (0, 0), nestPosition: (0, 0), explorationChance: 1.0);
        var rng = new Random(1);

        for (int i = 0; i < 300; i++)
        {
            ant.Step(Array.Empty<Agent>(), rng, grid);
            Assert.InRange(ant.Position.X, 0, 9);
            Assert.InRange(ant.Position.Y, 0, 9);
        }
    }

    [Fact]
    public void Toroidal_AntWrapsAroundTheEdge()
    {
        var grid = new Grid2D(10, 10, GridTopology.Toroidal);
        var ant = new AntAgent(0, (0, 0), nestPosition: (5, 5), explorationChance: 1.0);

        // Exploring with a roll of 0 takes the first candidate: up-left.
        ant.Step(Array.Empty<Agent>(), new FixedRandom(0.0), grid);

        Assert.Equal((9, 9), ant.Position);
    }
}
