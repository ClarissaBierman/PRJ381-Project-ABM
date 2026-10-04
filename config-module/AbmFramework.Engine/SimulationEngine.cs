using ABM.Core;
using AbmFramework.Config;
using AbmFramework.Engine.Scheduler;

namespace AbmFramework.Engine;

public sealed class SimulationEngine : ISimulationEngine
{
    private readonly List<Agent> _agents = new();

    private Grid2D? _grid;

    private SimulationConfig? _config;

    private IScheduler? _scheduler;

    private Random? _random;

    private int _nextAgentId;

    private SimulationState _state = SimulationState.Stopped;

    private CancellationTokenSource? _pauseTokenSource;

    public event EventHandler<TickStatistics>? TickCompleted;

    public event EventHandler? SimulationCompleted;

    public SimulationState State => _state;

    public bool IsRunning => _state == SimulationState.Running;

    public bool IsPaused => _state == SimulationState.Paused;

    public int CurrentTick { get; private set; }

    private volatile int _tickDelayMs = 50;

    //Delay between ticks in StartAsync, in milliseconds. Can be changed while
    //a simulation is running (e.g. from a dashboard speed slider).
    public int TickDelayMs
    {
        get => _tickDelayMs;
        set => _tickDelayMs = Math.Clamp(value, 0, 10_000);
    }

    //Creates an empty simulation engine.
    //The actual simulation is built when StartAsync() is called.
    public SimulationEngine()
    {
    }

    //Creates all objects required for a simulation run.
    private void InitialiseSimulation(SimulationConfig config)
    {
        _config = config;

        CurrentTick = 0;

        _agents.Clear();
        _nextAgentId = 0;

        _random = config.RandomSeed.HasValue
            ? new Random(config.RandomSeed.Value)
            : new Random();

        _grid = new Grid2D(
            config.GridWidth,
            config.GridHeight,
            config.Topology == AbmFramework.Config.GridTopology.Toroidal
                ? ABM.Core.GridTopology.Toroidal
                : ABM.Core.GridTopology.Bounded);

        _scheduler = config.Scheduler switch
        {
            SchedulerKind.Sequential => new SequentialScheduler(),
            SchedulerKind.Random => new RandomScheduler(),
            _ => throw new InvalidOperationException("Unknown scheduler.")
        };

        CreateAgents();
        _nextAgentId = _agents.Count;

        if (config.Model == ModelType.SIR)
        {
            InfectInitialAgents();
        }

        _state = SimulationState.Running;
    }

    //Creates every agent for the configured model and places it on the grid.
    private void CreateAgents()
    {
        if (_config is null || _grid is null || _random is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        switch (_config.Model)
        {
            case ModelType.SIR:
                CreateSIRAgents();
                break;

            case ModelType.Schelling:
                CreateSchellingAgents();
                break;

            case ModelType.Boids:
                CreateBoidAgents();
                break;

            case ModelType.AntForaging:
                CreateAntAgents();
                break;

            case ModelType.WolfSheep:
                CreateWolfSheepAgents();
                break;

            default:
                throw new InvalidOperationException($"Unknown model type: {_config.Model}");
        }
    }

    private void CreateWolfSheepAgents()
    {
        for (int y = 0; y < _config!.GridHeight; y++)
        {
            for (int x = 0; x < _config.GridWidth; x++)
            {
                _grid!.GetPatch(x, y).SetProperty("grass", true);
            }
        }

        for (int id = 0; id < _config.InitialSheep; id++)
        {
            var position = (_random!.Next(_config.GridWidth), _random.Next(_config.GridHeight));
            var sheep = new SheepAgent(
                id,
                position,
                _config.InitialSheepEnergy,
                _config.EnergyLossPerTick,
                _config.GrassEnergyGain,
                _config.SheepReproductionProbability);

            _agents.Add(sheep);
            _grid!.Place(sheep);
        }

        for (int id = 0; id < _config.InitialWolves; id++)
        {
            var position = (_random!.Next(_config.GridWidth), _random.Next(_config.GridHeight));
            var wolf = new WolfAgent(
                _config.InitialSheep + id,
                position,
                _config.InitialWolfEnergy,
                _config.EnergyLossPerTick,
                _config.SheepEnergyGain,
                _config.WolfReproductionProbability);

            _agents.Add(wolf);
            _grid!.Place(wolf);
        }
    }

    private void CreateSIRAgents()
    {
        var occupied = new HashSet<(int X, int Y)>();

        for (int id = 0; id < _config!.AgentCount; id++)
        {
            var position = NextEmptyPosition(occupied);

            var agent = new SIRAgent(
                id,
                position,
                _config.InfectionProbability,
                _config.RecoveryTicks);

            _agents.Add(agent);
            _grid!.Place(agent);
        }
    }

    private void CreateSchellingAgents()
    {
        var occupied = new HashSet<(int X, int Y)>();
        int groupACount = (int)Math.Round(_config!.AgentCount * _config.GroupARatio);

        for (int id = 0; id < _config.AgentCount; id++)
        {
            var position = NextEmptyPosition(occupied);
            int group = id < groupACount ? 0 : 1;

            var agent = new SchellingAgent(id, position, group, _config.SimilarityThreshold);

            _agents.Add(agent);
            _grid!.Place(agent);
        }
    }

    private void CreateBoidAgents()
    {
        for (int id = 0; id < _config!.AgentCount; id++)
        {
            var position = (_random!.Next(_config.GridWidth), _random.Next(_config.GridHeight));

            var agent = new BoidAgent(
                id,
                position,
                _config.GridWidth,
                _config.GridHeight,
                _grid!.Topology,
                _config.PerceptionRadius,
                _config.SeparationWeight,
                _config.AlignmentWeight,
                _config.CohesionWeight,
                _config.MaxSpeed);

            _agents.Add(agent);
            _grid.Place(agent);
        }
    }

    private void CreateAntAgents()
    {
        var nest = (X: _config!.GridWidth / 2, Y: _config.GridHeight / 2);
        _grid!.GetPatch(nest.X, nest.Y).SetProperty("nest", true);

        int minDistanceFromNest = Math.Min(3, Math.Min(_config.GridWidth, _config.GridHeight) / 4);

        for (int i = 0; i < _config.FoodSources; i++)
        {
            (int X, int Y) centre;
            int attempts = 0;
            do
            {
                centre = (_random!.Next(_config.GridWidth), _random.Next(_config.GridHeight));
                attempts++;
            }
            while (ChebyshevDistance(centre, nest) < minDistanceFromNest && attempts < 200);

            PlaceFoodCluster(centre, nest, _config.FoodPerSource);
        }

        for (int id = 0; id < _config.AgentCount; id++)
        {
            var agent = new AntAgent(
                id,
                nest,
                nest,
                _config.PheromoneDepositAmount,
                _config.ExplorationChance);

            _agents.Add(agent);
            _grid!.Place(agent);
        }
    }

    //Spreads one food source over a 3x3 patch (like NetLogo's food piles),
    //so ants can actually stumble onto it. Adds to any food already there,
    //so overlapping sources never lose food and the total stays exact.
    private void PlaceFoodCluster((int X, int Y) centre, (int X, int Y) nest, int amount)
    {
        var cells = new List<(int X, int Y)>();
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var cell = _grid!.ResolvePosition((centre.X + dx, centre.Y + dy));
                if (cell != null && cell.Value != nest && !cells.Contains(cell.Value))
                {
                    cells.Add(cell.Value);
                }
            }
        }

        if (cells.Count == 0) return;

        int share = amount / cells.Count;
        int remainder = amount % cells.Count;

        for (int i = 0; i < cells.Count; i++)
        {
            var patch = _grid!.GetPatch(cells[i].X, cells[i].Y);
            int existing = patch.GetProperty("food") is int f ? f : 0;
            patch.SetProperty("food", existing + share + (i < remainder ? 1 : 0));
        }
    }

    private static int ChebyshevDistance((int X, int Y) a, (int X, int Y) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    //Finds a random grid cell not already in the occupied set, and records it.
    private (int X, int Y) NextEmptyPosition(HashSet<(int X, int Y)> occupied)
    {
        (int X, int Y) position;

        do
        {
            position =
            (
                _random!.Next(_config!.GridWidth),
                _random.Next(_config.GridHeight)
            );
        }
        while (!occupied.Add(position));

        return position;
    }

    //Infects the configured number of agents at random. SIR model only.
    private void InfectInitialAgents()
    {
        if (_config is null || _random is null)
            throw new InvalidOperationException();

        var selected = _agents
            .OfType<SIRAgent>()
            .OrderBy(_ => _random.Next())
            .Take(_config.InitialInfected);

        foreach (var agent in selected)
        {
            agent.Infect();
        }
    }

    //Starts the simulation and executes ticks until the configured tick limit is reached or cancellation is requested.
    public async Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                "A simulation is already running.");

        InitialiseSimulation(config);

        while (!cancellationToken.IsCancellationRequested &&
               CurrentTick < config.TickLimit)
        {
            //Pause handling
            while (IsPaused && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            ExecuteTick();

            var statistics = BuildStatistics();

            TickCompleted?.Invoke(this, statistics);

            CurrentTick++;

            //Pause between ticks so dashboards can keep up. Adjustable while running.
            await Task.Delay(TickDelayMs, cancellationToken);
        }

        _state = SimulationState.Completed;

        SimulationCompleted?.Invoke(this, EventArgs.Empty);
    }

    //Runs a simulation to completion with no per-tick delay, returning the
    //full tick-by-tick statistics history. Used for batch / Monte Carlo
    //runs, where many replicates need to finish as fast as possible rather
    //than at dashboard-watchable speed.
    public async Task<IReadOnlyList<TickStatistics>> RunToCompletionAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                "A simulation is already running.");

        InitialiseSimulation(config);

        var history = new List<TickStatistics>();

        while (!cancellationToken.IsCancellationRequested &&
               CurrentTick < config.TickLimit)
        {
            ExecuteTick();

            var statistics = BuildStatistics();
            history.Add(statistics);

            TickCompleted?.Invoke(this, statistics);

            CurrentTick++;

            // Yield occasionally so a caller awaiting this (e.g. a batch
            // runner reporting progress) isn't blocked for the whole run.
            if (CurrentTick % 50 == 0)
            {
                await Task.Yield();
            }
        }

        _state = SimulationState.Completed;

        SimulationCompleted?.Invoke(this, EventArgs.Empty);

        return history;
    }

    //Executes a single simulation tick.
    //Each agent is given an opportunity to perform its behaviour according to the configured scheduler.
    private void ExecuteTick()
    {
        if (_scheduler is null || _grid is null || _random is null || _config is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        foreach (var agent in _scheduler.OrderAgents(_agents, _random).ToList())
        {
            if (!agent.IsAlive)
            {
                RemoveAgent(agent);
                continue;
            }

            var oldPosition = agent.Position;

            //Obtain neighbouring agents from the grid.
            var neighbours = _grid.GetNeighbours(agent);

            //Allow the agent to perform one simulation step. The grid is
            //passed in as the IEnvironmentManager so models that need
            //shared environment state (e.g. Ant Foraging's pheromone
            //trails) can read and write patches directly.
            agent.Step(neighbours, _random, _grid);

            //SchellingAgent cannot relocate itself - it has no view of the
            //whole grid, only its immediate neighbours - so the engine
            //(which owns the grid) moves it to a random empty cell when it
            //flags itself unhappy.
            if (agent.IsAlive && agent is SchellingAgent schellingAgent && !schellingAgent.IsHappy)
            {
                var empty = _grid.FindRandomEmptyPosition(_random);
                if (empty != null)
                {
                    agent.Position = empty.Value;
                }
            }

            //Keep the grid's occupant lists in sync with any position change.
            if (!agent.IsAlive)
            {
                RemoveAgent(agent);
            }
            else if (agent.Position != oldPosition)
            {
                _grid.Move(agent, oldPosition);
            }

            foreach (var offspring in agent.CreatePendingOffspring(() => _nextAgentId++))
            {
                _agents.Add(offspring);
                _grid.Place(offspring);
            }

            foreach (var deadAgent in _agents.Where(a => !a.IsAlive).ToList())
            {
                RemoveAgent(deadAgent);
            }
        }

        if (_config.Model == ModelType.AntForaging)
        {
            DecayPheromones();
        }
        else if (_config.Model == ModelType.WolfSheep)
        {
            RegrowGrass();
        }
    }

    private void RemoveAgent(Agent agent)
    {
        _grid!.Remove(agent);
        _agents.Remove(agent);
    }

    private void RegrowGrass()
    {
        if (_grid is null || _config is null || _random is null) return;

        foreach (var patch in _grid.AllPatches())
        {
            if (patch.GetProperty("grass") is false &&
                _random.NextDouble() < _config.GrassRegrowthProbability)
            {
                patch.SetProperty("grass", true);
            }
        }
    }

    //Decays every patch's pheromone level by the configured rate. This has
    //to live here rather than in AntAgent.Step() because it applies to
    //every visited patch each tick, not just the one the acting ant is on.
    private void DecayPheromones()
    {
        if (_grid is null || _config is null) return;

        foreach (var patch in _grid.AllPatches())
        {
            if (patch.GetProperty("pheromone") is double pheromone && pheromone > 0)
            {
                var decayed = pheromone * (1 - _config.PheromoneDecayRate);
                patch.SetProperty("pheromone", decayed < 0.01 ? 0.0 : decayed);
            }
        }
    }

    //Calculates the statistics for the configured model.
    private TickStatistics BuildStatistics()
    {
        if (_config is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        return _config.Model switch
        {
            ModelType.SIR => BuildSIRStatistics(),
            ModelType.Schelling => BuildSchellingStatistics(),
            ModelType.Boids => BuildBoidStatistics(),
            ModelType.AntForaging => BuildAntStatistics(),
            ModelType.WolfSheep => BuildWolfSheepStatistics(),
            _ => throw new InvalidOperationException($"Unknown model type: {_config.Model}")
        };
    }

    private TickStatistics BuildWolfSheepStatistics()
    {
        int sheep = _agents.OfType<SheepAgent>().Count();
        int wolves = _agents.OfType<WolfAgent>().Count();
        int grass = _grid?.AllPatches().Count(p => p.GetProperty("grass") is true) ?? 0;

        return new TickStatistics
        {
            Tick = CurrentTick,
            TotalPopulation = sheep + wolves,
            Metrics = new Dictionary<string, double>
            {
                ["Sheep"] = sheep,
                ["Wolves"] = wolves,
                ["Grass"] = grass
            }
        };
    }

    private TickStatistics BuildSIRStatistics()
    {
        int susceptible = 0;
        int infected = 0;
        int recovered = 0;

        foreach (var agent in _agents.OfType<SIRAgent>())
        {
            switch (agent.State)
            {
                case HealthState.Susceptible:
                    susceptible++;
                    break;

                case HealthState.Infected:
                    infected++;
                    break;

                case HealthState.Recovered:
                    recovered++;
                    break;
            }
        }

        return new TickStatistics
        {
            Tick = CurrentTick,
            Susceptible = susceptible,
            Infected = infected,
            Recovered = recovered,
            TotalPopulation = _agents.Count
        };
    }

    private TickStatistics BuildSchellingStatistics()
    {
        int happy = _agents.OfType<SchellingAgent>().Count(a => a.IsHappy);
        int total = _agents.Count;

        return new TickStatistics
        {
            Tick = CurrentTick,
            TotalPopulation = total,
            Metrics = new Dictionary<string, double>
            {
                ["HappyAgents"] = happy,
                ["UnhappyAgents"] = total - happy,
                ["HappyFraction"] = total == 0 ? 0 : (double)happy / total
            }
        };
    }

    private TickStatistics BuildBoidStatistics()
    {
        var boids = _agents.OfType<BoidAgent>().ToList();

        double averageSpeed = boids.Count == 0
            ? 0
            : boids.Average(b => Math.Sqrt(b.VX * b.VX + b.VY * b.VY));

        double centreX = boids.Count == 0 ? 0 : boids.Average(b => b.X);
        double centreY = boids.Count == 0 ? 0 : boids.Average(b => b.Y);

        double flockRadius = boids.Count == 0
            ? 0
            : Math.Sqrt(boids.Average(b => Math.Pow(b.X - centreX, 2) + Math.Pow(b.Y - centreY, 2)));

        return new TickStatistics
        {
            Tick = CurrentTick,
            TotalPopulation = _agents.Count,
            Metrics = new Dictionary<string, double>
            {
                ["AverageSpeed"] = averageSpeed,
                ["FlockRadius"] = flockRadius
            }
        };
    }

    private TickStatistics BuildAntStatistics()
    {
        int carryingFood = _agents.OfType<AntAgent>().Count(a => a.CarryingFood);

        double foodRemaining = _grid?.AllPatches()
            .Select(p => p.GetProperty("food") is int f ? f : 0)
            .Sum() ?? 0;

        double foodCollected = (_config!.FoodSources * _config.FoodPerSource) - foodRemaining;

        return new TickStatistics
        {
            Tick = CurrentTick,
            TotalPopulation = _agents.Count,
            Metrics = new Dictionary<string, double>
            {
                ["FoodCollected"] = foodCollected,
                ["FoodRemaining"] = foodRemaining,
                ["AntsCarryingFood"] = carryingFood
            }
        };
    }

    //Returns a snapshot of the current simulation.
    public SimulationSnapshot GetSnapshot()
    {
        if (_config is null || _grid is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        return new SimulationSnapshot
        {
            Configuration = _config,
            Grid = _grid,
            Agents = _agents.AsReadOnly(),
            Statistics = BuildStatistics(),
            State = _state
        };
    }

    //Pauses the simulation.
    public void Pause()
    {
        if (_state != SimulationState.Running)
            return;

        _state = SimulationState.Paused;
    }

    //Resumes a paused simulation.
    public void Resume()
    {
        if (_state != SimulationState.Paused)
            return;

        _state = SimulationState.Running;
    }

    //Stops the simulation and clears all runtime state.
    public void Reset()
    {
        _state = SimulationState.Stopped;

        CurrentTick = 0;

        _agents.Clear();
        _nextAgentId = 0;

        _grid = null;
        _config = null;
        _scheduler = null;
        _random = null;

        _pauseTokenSource?.Dispose();
        _pauseTokenSource = null;
    }
}
