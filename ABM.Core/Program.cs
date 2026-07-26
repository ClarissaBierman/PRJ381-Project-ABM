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