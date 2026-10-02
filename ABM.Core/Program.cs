using ABM.Core;

var rng = new Random(42);
var grid = new Grid2D(width: 10, height: 10);

var agents = new List<SIRAgent>();
for (int i = 0; i < 10; i++)
{
    agents.Add(new SIRAgent(i, (i, 0), infectionProbability: 0.4, recoveryTime: 50));
}
agents[0].Infect(); // patient zero

for (int tick = 0; tick < 20; tick++)
{
    foreach (var agent in agents)
    {
        var others = agents.Where(a => a != agent).Cast<Agent>().ToList();
        agent.Step(others, rng, grid);
    }

    int s = agents.Count(a => a.State == HealthState.Susceptible);
    int i = agents.Count(a => a.State == HealthState.Infected);
    int r = agents.Count(a => a.State == HealthState.Recovered);
    Console.WriteLine($"Tick {tick}: S={s} I={i} R={r}");
}

Console.WriteLine();
Console.WriteLine("--- Grid/Patch Test ---");

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

// --- Boid smoke test ---
Console.WriteLine();
Console.WriteLine("--- Boid Flocking Test ---");

var boidGrid = new Grid2D(width: 30, height: 30, GridTopology.Toroidal);
var boidRng = new Random(1);
var boids = new List<BoidAgent>();
for (int i = 0; i < 15; i++)
{
    var pos = (boidRng.Next(30), boidRng.Next(30));
    var boid = new BoidAgent(i, pos, gridWidth: 30, gridHeight: 30, topology: GridTopology.Toroidal);
    boids.Add(boid);
    boidGrid.Place(boid);
}

for (int tick = 0; tick < 10; tick++)
{
    foreach (var boid in boids)
    {
        var oldPos = boid.Position;
        var neighbours = boidGrid.GetNeighbours(boid, moore: true).Cast<Agent>().ToList();
        boid.Step(neighbours, boidRng, boidGrid);
        boidGrid.Move(boid, oldPos);
    }
}
double avgX = boids.Average(b => b.X);
double avgY = boids.Average(b => b.Y);
Console.WriteLine($"After 10 ticks, flock centre is roughly ({avgX:F1}, {avgY:F1})");

// --- Schelling smoke test ---
Console.WriteLine();
Console.WriteLine("--- Schelling Segregation Test ---");

var schellingGrid = new Grid2D(width: 10, height: 10, GridTopology.Bounded);
var schellingRng = new Random(2);
var schellingAgents = new List<SchellingAgent>();
var occupied = new HashSet<(int, int)>();
for (int i = 0; i < 60; i++)
{
    (int X, int Y) pos;
    do { pos = (schellingRng.Next(10), schellingRng.Next(10)); } while (!occupied.Add(pos));
    var agent = new SchellingAgent(i, pos, group: i % 2, similarityThreshold: 0.5);
    schellingAgents.Add(agent);
    schellingGrid.Place(agent);
}

for (int tick = 0; tick < 10; tick++)
{
    foreach (var agent in schellingAgents)
    {
        var neighbours = schellingGrid.GetNeighbours(agent, moore: true).Cast<Agent>().ToList();
        agent.Step(neighbours, schellingRng, schellingGrid);

        if (!agent.IsHappy)
        {
            var empty = schellingGrid.FindRandomEmptyPosition(schellingRng);
            if (empty != null)
            {
                var oldPos = agent.Position;
                agent.Position = empty.Value;
                schellingGrid.Move(agent, oldPos);
            }
        }
    }
}
int happyCount = schellingAgents.Count(a => a.IsHappy);
Console.WriteLine($"After 10 ticks, {happyCount}/{schellingAgents.Count} agents are happy");

// --- Ant foraging smoke test ---
Console.WriteLine();
Console.WriteLine("--- Ant Foraging Test ---");

var antGrid = new Grid2D(width: 20, height: 20, GridTopology.Bounded);
var antRng = new Random(3);
var nest = (10, 10);
antGrid.SetPatchProperty(3, 3, "food", 20);
antGrid.SetPatchProperty(16, 16, "food", 20);

var ants = new List<AntAgent>();
for (int i = 0; i < 10; i++)
{
    var ant = new AntAgent(i, nest, nest, pheromoneDepositAmount: 5.0, explorationChance: 0.2);
    ants.Add(ant);
    antGrid.Place(ant);
}

for (int tick = 0; tick < 300; tick++)
{
    foreach (var ant in ants)
    {
        var oldPos = ant.Position;
        var neighbours = antGrid.GetNeighbours(ant, moore: true).Cast<Agent>().ToList();
        ant.Step(neighbours, antRng, antGrid);
        antGrid.Move(ant, oldPos);
    }
}

int foodRemaining =
    (antGrid.GetPatch(3, 3).GetProperty("food") is int f1 ? f1 : 0) +
    (antGrid.GetPatch(16, 16).GetProperty("food") is int f2 ? f2 : 0);
Console.WriteLine($"After 300 ticks, {40 - foodRemaining} food units collected out of 40");
