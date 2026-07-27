using ABM.Core;

var rng = new Random(42);

var agents = new List<SIRAgent>();
for (int i = 0; i < 10; i++)
{
    agents.Add(new SIRAgent(i, (i, 0), infectionProbability: 0.05, recoveryTime: 5));
}
agents[0].Infect(); // patient zero

for (int tick = 0; tick < 20; tick++)
{
    foreach (var agent in agents)
    {
        var others = agents.Where(a => a != agent).Cast<Agent>().ToList();
        agent.Step(others, rng);
    }

    int s = agents.Count(a => a.State == HealthState.Susceptible);
    int i = agents.Count(a => a.State == HealthState.Infected);
    int r = agents.Count(a => a.State == HealthState.Recovered);
    Console.WriteLine($"Tick {tick}: S={s} I={i} R={r}");
}

Console.WriteLine();
Console.WriteLine("--- Grid/Patch Test ---");

var grid = new Grid2D(width: 10, height: 10);
grid.SetPatchProperty(5, 5, "resourceCount", 10);
var patch = grid.GetPatch(5, 5);
Console.WriteLine($"Patch (5,5) resourceCount = {patch.GetProperty("resourceCount")}");

var testAgent = new SIRAgent(99, (5, 5), infectionProbability: 0, recoveryTime: 1);
grid.Place(testAgent);
var neighboursByCoord = grid.GetNeighbours(5, 5, moore: true);
Console.WriteLine($"Coordinate-based GetNeighbours(5,5) found {neighboursByCoord.Count} agent(s)");

// Place a second agent adjacent to the first, to prove GetNeighbours actually finds occupants
var adjacentAgent = new SIRAgent(100, (5, 6), infectionProbability: 0, recoveryTime: 1);
grid.Place(adjacentAgent);

var neighboursAfterAdd = grid.GetNeighbours(5, 5, moore: true);
Console.WriteLine($"After placing an agent at (5,6), GetNeighbours(5,5) now found {neighboursAfterAdd.Count} agent(s) (expect 1)");