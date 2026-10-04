using Xunit;

namespace ABM.Core.Tests;

public class SchellingAgentTests
{
    private readonly Grid2D grid = new(10, 10, GridTopology.Bounded);
    private readonly Random rng = new FixedRandom(0.5);

    private static Agent[] Neighbours(int sameGroup, int otherGroup)
    {
        var neighbours = new List<Agent>();
        for (int i = 0; i < sameGroup; i++) neighbours.Add(new SchellingAgent(100 + i, (0, 0), group: 0, similarityThreshold: 0.5));
        for (int i = 0; i < otherGroup; i++) neighbours.Add(new SchellingAgent(200 + i, (0, 0), group: 1, similarityThreshold: 0.5));
        return neighbours.ToArray();
    }

    [Theory]
    [InlineData(3, 1, 0.5, true)]   // 75% similar, needs 50%
    [InlineData(2, 2, 0.5, true)]   // exactly at the threshold counts as happy
    [InlineData(1, 3, 0.5, false)]  // 25% similar, needs 50%
    [InlineData(2, 1, 0.7, false)]  // 67% similar, needs 70%
    [InlineData(0, 4, 0.0, true)]   // threshold 0 is always happy
    public void Step_SetsHappinessFromShareOfSameGroupNeighbours(
        int sameGroup, int otherGroup, double threshold, bool expectedHappy)
    {
        var agent = new SchellingAgent(0, (5, 5), group: 0, similarityThreshold: threshold);

        agent.Step(Neighbours(sameGroup, otherGroup), rng, grid);

        Assert.Equal(expectedHappy, agent.IsHappy);
    }

    [Fact]
    public void Step_WithNoNeighbours_IsHappy()
    {
        var agent = new SchellingAgent(0, (5, 5), group: 0, similarityThreshold: 1.0);

        agent.Step(Array.Empty<Agent>(), rng, grid);

        Assert.True(agent.IsHappy);
    }

    [Fact]
    public void Step_IgnoresNonSchellingNeighbours()
    {
        var agent = new SchellingAgent(0, (5, 5), group: 0, similarityThreshold: 0.6);
        var neighbours = Neighbours(sameGroup: 1, otherGroup: 0)
            .Append(new SIRAgent(300, (5, 6), 0.5, 3))
            .ToArray();

        agent.Step(neighbours, rng, grid);

        Assert.True(agent.IsHappy);
    }

    [Fact]
    public void Step_BecomingUnhappy_DoesNotMoveTheAgent()
    {
        // Relocation is the engine's job, not the agent's.
        var agent = new SchellingAgent(0, (5, 5), group: 0, similarityThreshold: 0.9);

        agent.Step(Neighbours(sameGroup: 0, otherGroup: 3), rng, grid);

        Assert.False(agent.IsHappy);
        Assert.Equal((5, 5), agent.Position);
    }

    [Fact]
    public void DisplayState_IncludesGroupAndHappiness()
    {
        var agent = new SchellingAgent(0, (5, 5), group: 1, similarityThreshold: 0.9);
        Assert.Equal("Group1-Happy", agent.DisplayState);

        agent.Step(Neighbours(sameGroup: 3, otherGroup: 0), rng, grid); // all group 0, so unhappy

        Assert.Equal("Group1-Unhappy", agent.DisplayState);
    }
}
