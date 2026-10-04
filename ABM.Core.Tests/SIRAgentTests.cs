using Xunit;

namespace ABM.Core.Tests;

public class SIRAgentTests
{
    private readonly Grid2D grid = new(10, 10, GridTopology.Bounded);

    private static SIRAgent Susceptible(double infectionProbability = 0.5, int recoveryTime = 3) =>
        new(0, (5, 5), infectionProbability, recoveryTime);

    private static SIRAgent Infected()
    {
        var agent = new SIRAgent(1, (5, 6), infectionProbability: 0.5, recoveryTime: 3);
        agent.Infect();
        return agent;
    }

    [Fact]
    public void NewAgent_StartsSusceptible()
    {
        Assert.Equal(HealthState.Susceptible, Susceptible().State);
    }

    [Fact]
    public void Susceptible_WithInfectedNeighbour_AndRollBelowProbability_BecomesInfected()
    {
        var agent = Susceptible(infectionProbability: 0.5);

        agent.Step(new Agent[] { Infected() }, new FixedRandom(0.0), grid);

        Assert.Equal(HealthState.Infected, agent.State);
    }

    [Fact]
    public void Susceptible_WithInfectedNeighbour_AndRollAboveProbability_StaysSusceptible()
    {
        var agent = Susceptible(infectionProbability: 0.5);

        agent.Step(new Agent[] { Infected() }, new FixedRandom(0.999), grid);

        Assert.Equal(HealthState.Susceptible, agent.State);
    }

    [Fact]
    public void Susceptible_WithZeroInfectionProbability_IsNeverInfected()
    {
        var agent = Susceptible(infectionProbability: 0.0);

        agent.Step(new Agent[] { Infected(), Infected() }, new FixedRandom(0.0), grid);

        Assert.Equal(HealthState.Susceptible, agent.State);
    }

    [Fact]
    public void Susceptible_WithOnlySusceptibleOrNonSirNeighbours_StaysSusceptible()
    {
        var agent = Susceptible(infectionProbability: 1.0);
        var neighbours = new Agent[]
        {
            Susceptible(),
            new SchellingAgent(2, (4, 5), group: 0, similarityThreshold: 0.5)
        };

        agent.Step(neighbours, new FixedRandom(0.0), grid);

        Assert.Equal(HealthState.Susceptible, agent.State);
    }

    [Fact]
    public void Infected_RecoversAfterExactlyRecoveryTimeSteps()
    {
        var agent = Infected();
        var rng = new FixedRandom(0.0);

        agent.Step(Array.Empty<Agent>(), rng, grid);
        agent.Step(Array.Empty<Agent>(), rng, grid);
        Assert.Equal(HealthState.Infected, agent.State);

        agent.Step(Array.Empty<Agent>(), rng, grid);
        Assert.Equal(HealthState.Recovered, agent.State);
    }

    [Fact]
    public void Recovered_IsImmuneToInfectedNeighbours()
    {
        var agent = new SIRAgent(0, (5, 5), infectionProbability: 1.0, recoveryTime: 1);
        agent.Infect();
        agent.Step(Array.Empty<Agent>(), new FixedRandom(0.0), grid);
        Assert.Equal(HealthState.Recovered, agent.State);

        agent.Step(new Agent[] { Infected() }, new FixedRandom(0.0), grid);

        Assert.Equal(HealthState.Recovered, agent.State);
    }

    [Fact]
    public void Step_NeverMovesTheAgent()
    {
        var agent = Susceptible();

        agent.Step(new Agent[] { Infected() }, new FixedRandom(0.0), grid);

        Assert.Equal((5, 5), agent.Position);
    }

    [Fact]
    public void DisplayState_MatchesHealthState()
    {
        Assert.Equal("Susceptible", Susceptible().DisplayState);
        Assert.Equal("Infected", Infected().DisplayState);
    }
}
