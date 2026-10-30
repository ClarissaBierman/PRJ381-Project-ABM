// =============================================================================
// SimulationEngine.cs  (AbmFramework.Engine)
//
// The heart of the framework: it builds a simulation from a SimulationConfig
// and runs it tick by tick.
//
// The life of a run:
//   1. InitialiseSimulation  - create the random generator (from the seed),
//                              the grid, the scheduler and all the agents.
//   2. Loop, once per tick:
//        ExecuteTick         - every agent takes its turn (Step), in the order
//                              the scheduler picks. Moves, deaths and births
//                              are synced to the grid. Then any whole-grid
//                              process runs (grass regrowth, pheromone decay).
//        BuildStatistics     - count what happened (S/I/R, sheep, food, ...).
//        TickCompleted event - lets the dashboard and the database react.
//   3. SimulationCompleted event when the tick limit is reached or the run is cancelled.
//
// Two ways to run:
//   StartAsync            - "live" mode for the dashboard. Waits TickDelayMs
//                           between ticks and supports Pause, Resume and Step.
//   RunToCompletionAsync  - "batch" mode for Monte Carlo. No delay, no pausing,
//                           returns the whole tick history at the end.
//
// Repeatability: everything random goes through ONE Random object made from
// config.RandomSeed. Same seed + same settings = exactly the same run. That is
// what makes Past runs, Continue and Compare trustworthy.
//
// Adding a new model touches this file in three places: CreateAgents,
// BuildStatistics and (if needed) the end of ExecuteTick.
// See docs/ADDING-A-MODEL.md.
// =============================================================================

using ABM.Core;
using AbmFramework.Config;
using AbmFramework.Engine.Scheduler;

namespace AbmFramework.Engine;

/// <summary>
/// Builds and runs one simulation at a time from a <see cref="SimulationConfig"/>.
/// Create a new engine for every run (the Monte Carlo runner does this) or call
/// <see cref="Reset"/> before reusing one.
/// </summary>
public sealed class SimulationEngine : ISimulationEngine
{
    // Every living agent in the run. Dead agents are removed during the tick.
    private readonly List<Agent> _agents = new();

    // These are all null until StartAsync / RunToCompletionAsync sets them up.
    private Grid2D? _grid;
    private SimulationConfig? _config;
    private IScheduler? _scheduler;
    private Random? _random;          // the ONE random generator for the whole run

    // Next free agent id, used for newborn agents.
    private int _nextAgentId;

    private SimulationState _state = SimulationState.Stopped;

    // Never assigned anywhere, only disposed in Reset(). A leftover from an
    // earlier pause design. See docs/KNOWN-ISSUES.md.
    private CancellationTokenSource? _pauseTokenSource;

    /// <summary>
    /// Raised after every tick with that tick's statistics. The dashboard uses
    /// it to push updates to the browser and to save the tick to the database.
    /// Note: it is raised on the engine's own thread, so handlers should be quick.
    /// </summary>
    public event EventHandler<TickStatistics>? TickCompleted;

    /// <summary>Raised once when the run ends (tick limit reached or cancelled).</summary>
    public event EventHandler? SimulationCompleted;

    /// <summary>
    /// Raised once the agents and grid exist, before the first tick runs.
    /// The dashboard uses it to draw tick 0.
    /// </summary>
    public event EventHandler? SimulationInitialised;

    /// <summary>
    /// When true, <see cref="StartAsync"/> sets everything up and then waits
    /// paused at tick 0 (the dashboard's Setup button does this).
    /// </summary>
    public bool StartPaused { get; set; }

    // Set by Step() to let exactly one tick through while paused.
    // volatile because Step() is called from a different thread (the web
    // request) than the one running the tick loop.
    private volatile bool _stepRequested;

    /// <summary>Running, Paused, Stopped or Completed.</summary>
    public SimulationState State => _state;

    /// <summary>True while ticks are running.</summary>
    public bool IsRunning => _state == SimulationState.Running;

    /// <summary>True while paused (between ticks).</summary>
    public bool IsPaused => _state == SimulationState.Paused;

    /// <summary>
    /// The number of ticks finished so far. Inside a TickCompleted handler it
    /// still holds the tick that just ran (it is increased after the event).
    /// </summary>
    public int CurrentTick { get; private set; }

    // volatile for the same reason as _stepRequested: the speed slider changes
    // it from another thread while the tick loop is reading it.
    private volatile int _tickDelayMs = 50;

    /// <summary>
    /// Delay between ticks in live mode (StartAsync), in milliseconds, from 0 to
    /// 10 000. Can be changed while running, which is how the dashboard's speed
    /// slider works.
    /// </summary>
    public int TickDelayMs
    {
        get => _tickDelayMs;
        set => _tickDelayMs = Math.Clamp(value, 0, 10_000);
    }

    /// <summary>
    /// Creates an empty engine. Nothing is built until <see cref="StartAsync"/>
    /// or <see cref="RunToCompletionAsync"/> is called with a config.
    /// </summary>
    public SimulationEngine()
    {
    }

    // Builds everything a run needs: random generator, grid, scheduler and agents.
    private void InitialiseSimulation(SimulationConfig config)
    {
        _config = config;

        CurrentTick = 0;

        _agents.Clear();
        _nextAgentId = 0;

        // A fixed seed makes the whole run repeatable. No seed = a different
        // run every time.
        _random = config.RandomSeed.HasValue
            ? new Random(config.RandomSeed.Value)
            : new Random();

        // Config and Core each have their own GridTopology enum (Config can't
        // depend on Core), so translate between them here.
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

        // Newborns get ids after the starting agents.
        _nextAgentId = _agents.Count;

        if (config.Model == ModelType.SIR)
        {
            InfectInitialAgents();
        }

        _state = SimulationState.Running;
    }

    // Creates every agent for the configured model and places it on the grid.
    // ADD A MODEL: add a case here that calls your own Create...Agents method.
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

    // Wolf-Sheep: grass on every cell, then sheep and wolves at random cells
    // (several animals may share a cell).
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
                _config.InitialSheep + id,   // wolf ids continue after the sheep ids
                position,
                _config.InitialWolfEnergy,
                _config.EnergyLossPerTick,
                _config.SheepEnergyGain,
                _config.WolfReproductionProbability);

            _agents.Add(wolf);
            _grid!.Place(wolf);
        }
    }

    // SIR: one person per cell, on random empty cells.
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

    // Schelling: one agent per cell. The first GroupARatio of the agents go in
    // group 0, the rest in group 1. Their cells are random, so the groups start mixed.
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

    // Boids: random cells (sharing allowed), with the flocking settings from the config.
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

    // Ant Foraging: the nest in the centre, food piles at random spots not too
    // close to the nest, and every ant starting at the nest.
    private void CreateAntAgents()
    {
        var nest = (X: _config!.GridWidth / 2, Y: _config.GridHeight / 2);
        _grid!.GetPatch(nest.X, nest.Y).SetProperty("nest", true);

        // Keep food at least 3 cells from the nest (less on tiny grids),
        // otherwise the ants find it instantly and there is nothing to watch.
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
            // Give up after 200 tries so a tiny grid can't loop forever.
            while (ChebyshevDistance(centre, nest) < minDistanceFromNest && attempts < 200);

            PlaceFoodCluster(centre, nest, _config.FoodPerSource);
        }

        for (int id = 0; id < _config.AgentCount; id++)
        {
            var agent = new AntAgent(
                id,
                nest,    // starting position
                nest,    // where home is
                _config.PheromoneDepositAmount,
                _config.ExplorationChance);

            _agents.Add(agent);
            _grid!.Place(agent);
        }
    }

    // Spreads one food source over a 3x3 patch (like NetLogo's food piles),
    // so ants can actually stumble onto it. Adds to any food already there,
    // so overlapping sources never lose food and the total stays exact.
    private void PlaceFoodCluster((int X, int Y) centre, (int X, int Y) nest, int amount)
    {
        // Collect the valid cells of the 3x3 block, skipping the nest itself
        // and any cell counted twice (possible after wrapping on a tiny grid).
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

        // Share the food out evenly. The leftover units go one each to the
        // first few cells, so the total is exactly "amount".
        int share = amount / cells.Count;
        int remainder = amount % cells.Count;

        for (int i = 0; i < cells.Count; i++)
        {
            var patch = _grid!.GetPatch(cells[i].X, cells[i].Y);
            int existing = patch.GetProperty("food") is int f ? f : 0;
            patch.SetProperty("food", existing + share + (i < remainder ? 1 : 0));
        }
    }

    // Distance in steps when diagonal moves are allowed.
    private static int ChebyshevDistance((int X, int Y) a, (int X, int Y) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // Finds a random grid cell not already in the occupied set, and records it.
    // Config validation guarantees there are enough cells, so this always ends.
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
        // HashSet.Add returns false if the cell was already taken, so keep trying.
        while (!occupied.Add(position));

        return position;
    }

    // Infects InitialInfected random people at the start. SIR model only.
    private void InfectInitialAgents()
    {
        if (_config is null || _random is null)
            throw new InvalidOperationException();

        // Shuffle (sort by a random number) and take the first few.
        var selected = _agents
            .OfType<SIRAgent>()
            .OrderBy(_ => _random.Next())
            .Take(_config.InitialInfected);

        foreach (var agent in selected)
        {
            agent.Infect();
        }
    }

    /// <summary>
    /// Live mode: builds the simulation and runs ticks until the tick limit or
    /// until <paramref name="cancellationToken"/> is cancelled. Waits
    /// <see cref="TickDelayMs"/> between ticks and honours Pause, Resume and Step.
    /// Used by the dashboard (LiveSimulation).
    /// </summary>
    /// <param name="config">The settings for the run. Should already be validated.</param>
    /// <param name="cancellationToken">Cancel to stop the run early (the Reset button does this).</param>
    /// <exception cref="InvalidOperationException">If this engine is already running.</exception>
    public async Task StartAsync(
        SimulationConfig config,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                "A simulation is already running.");

        InitialiseSimulation(config);

        if (StartPaused)
            _state = SimulationState.Paused;

        SimulationInitialised?.Invoke(this, EventArgs.Empty);

        while (!cancellationToken.IsCancellationRequested &&
               CurrentTick < config.TickLimit)
        {
            // While paused, check every 20 ms whether we should carry on.
            // A step request lets exactly one tick through.
            while (IsPaused && !_stepRequested && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(20, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            bool stepping = _stepRequested;
            _stepRequested = false;

            ExecuteTick();

            var statistics = BuildStatistics();

            TickCompleted?.Invoke(this, statistics);

            CurrentTick++;

            // Pause between ticks so dashboards can keep up. Skipped for a
            // single step so the Step button responds instantly.
            if (!stepping)
                await Task.Delay(TickDelayMs, cancellationToken);
        }

        _state = SimulationState.Completed;

        SimulationCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Batch mode: runs a whole simulation as fast as possible (no delay, no
    /// pausing) and returns the statistics of every tick. Used for Monte Carlo
    /// and Compare, where many runs need to finish quickly.
    /// </summary>
    /// <param name="config">The settings for the run. Should already be validated.</param>
    /// <param name="cancellationToken">Cancel to stop early. The history so far is returned.</param>
    /// <returns>One <see cref="TickStatistics"/> per tick, in order.</returns>
    /// <exception cref="InvalidOperationException">If this engine is already running.</exception>
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

    // Runs one tick: every agent takes its turn in scheduler order, the grid is
    // kept in sync, then any whole-grid process for the model runs.
    private void ExecuteTick()
    {
        if (_scheduler is null || _grid is null || _random is null || _config is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        // ToList() takes a copy of the order, so agents being added or removed
        // during the loop can't break the foreach.
        foreach (var agent in _scheduler.OrderAgents(_agents, _random).ToList())
        {
            // An agent killed earlier this tick (for example a sheep eaten by
            // a wolf) doesn't get a turn.
            if (!agent.IsAlive)
            {
                RemoveAgent(agent);
                continue;
            }

            var oldPosition = agent.Position;

            // Obtain neighbouring agents from the grid.
            var neighbours = _grid.GetNeighbours(agent);

            // Allow the agent to perform one simulation step. The grid is
            // passed in as the IEnvironmentManager so models that need
            // shared environment state (e.g. Ant Foraging's pheromone
            // trails) can read and write patches directly.
            agent.Step(neighbours, _random, _grid);

            // SchellingAgent cannot relocate itself (it has no view of the
            // whole grid, only its immediate neighbours), so the engine,
            // which owns the grid, moves it to a random empty cell when it
            // flags itself unhappy.
            if (agent.IsAlive && agent is SchellingAgent schellingAgent && !schellingAgent.IsHappy)
            {
                var empty = _grid.FindRandomEmptyPosition(_random);
                if (empty != null)
                {
                    agent.Position = empty.Value;
                }
            }

            // Keep the grid's occupant lists in sync with any position change.
            if (!agent.IsAlive)
            {
                RemoveAgent(agent);
            }
            else if (agent.Position != oldPosition)
            {
                _grid.Move(agent, oldPosition);
            }

            // Add any babies this agent asked for, with fresh ids.
            foreach (var offspring in agent.CreatePendingOffspring(() => _nextAgentId++))
            {
                _agents.Add(offspring);
                _grid.Place(offspring);
            }

            // Clean up anything this agent killed (for example the sheep a
            // wolf just ate), so it disappears from the grid straight away.
            foreach (var deadAgent in _agents.Where(a => !a.IsAlive).ToList())
            {
                RemoveAgent(deadAgent);
            }
        }

        // Whole-grid processes that don't belong to any single agent.
        // ADD A MODEL: if your model changes the environment by itself each
        // tick, add it here.
        if (_config.Model == ModelType.AntForaging)
        {
            DecayPheromones();
        }
        else if (_config.Model == ModelType.WolfSheep)
        {
            RegrowGrass();
        }
    }

    // Takes an agent out of both the grid and the agent list.
    private void RemoveAgent(Agent agent)
    {
        _grid!.Remove(agent);
        _agents.Remove(agent);
    }

    // Every eaten cell has a GrassRegrowthProbability chance of growing back this tick.
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

    // Decays every patch's pheromone level by the configured rate. This has
    // to live here rather than in AntAgent.Step() because it applies to
    // every visited patch each tick, not just the one the acting ant is on.
    // Tiny amounts (below 0.01) are rounded to 0 so old trails fully disappear.
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

    // Counts what happened this tick, for the configured model.
    // ADD A MODEL: add a case here with your own Build...Statistics method.
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

    // Wolf-Sheep: number of sheep, wolves and cells with grass.
    // The metric names ("Sheep", "Wolves", "Grass") are what the chart,
    // the database and the exports show, so keep them stable.
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

    // SIR: how many people are Susceptible, Infected and Recovered.
    // SIR is the original model, so it has its own fields on TickStatistics
    // instead of using the Metrics dictionary.
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

    // Schelling: happy and unhappy counts, plus the happy fraction (0 to 1).
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

    // Boids: average speed, and the flock radius (how spread out the boids
    // are: the root-mean-square distance from their centre point). A smaller
    // radius means a tighter flock.
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

    // Ant Foraging: food still on the grid, food collected so far, and how
    // many ants are carrying food right now. Collected = starting total minus
    // what is left, which counts food being carried as collected.
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

    /// <summary>
    /// A read-only look at the current state of the run: config, grid, agents
    /// and the latest statistics. The dashboard uses it to draw the grid.
    /// </summary>
    /// <exception cref="InvalidOperationException">If no run has been set up yet.</exception>
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

    /// <summary>Pauses a running simulation after the current tick. Does nothing if it isn't running.</summary>
    public void Pause()
    {
        if (_state != SimulationState.Running)
            return;

        _state = SimulationState.Paused;
    }

    /// <summary>Resumes a paused simulation. Does nothing if it isn't paused.</summary>
    public void Resume()
    {
        if (_state != SimulationState.Paused)
            return;

        _state = SimulationState.Running;
    }

    /// <summary>
    /// Runs exactly one tick while paused, like NetLogo's "go once" button.
    /// Does nothing if the simulation isn't paused.
    /// </summary>
    public void Step()
    {
        if (_state == SimulationState.Paused)
            _stepRequested = true;
    }

    /// <summary>
    /// Clears everything so the engine can be used again. Note that this does
    /// not stop a running StartAsync loop by itself: cancel its token for that.
    /// </summary>
    public void Reset()
    {
        _state = SimulationState.Stopped;
        _stepRequested = false;

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
