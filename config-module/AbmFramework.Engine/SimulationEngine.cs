using ABM.Core;
using AbmFramework.Config;
using AbmFramework.Engine.Scheduler;

namespace AbmFramework.Engine;

public sealed class SimulationEngine : ISimulationEngine
{
    private readonly List<SIRAgent> _agents = new();

    private Grid2D? _grid;

    private SimulationConfig? _config;

    private IScheduler? _scheduler;

    private Random? _random;

    private SimulationState _state = SimulationState.Stopped;

    private CancellationTokenSource? _pauseTokenSource;

    public event EventHandler<TickStatistics>? TickCompleted;

    public event EventHandler? SimulationCompleted;

    public SimulationState State => _state;

    public bool IsRunning => _state == SimulationState.Running;

    public bool IsPaused => _state == SimulationState.Paused;

    public int CurrentTick { get; private set; }

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

        InfectInitialAgents();

        _state = SimulationState.Running;
    }

    //Creates every agent and places it randomly on the grid.
    private void CreateAgents()
    {
        if (_config is null || _grid is null || _random is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        var occupied = new HashSet<(int X, int Y)>();

        for (int id = 0; id < _config.AgentCount; id++)
        {
            (int X, int Y) position;

            do
            {
                position =
                (
                    _random.Next(_config.GridWidth),
                    _random.Next(_config.GridHeight)
                );
            }
            while (!occupied.Add(position));

            var agent = new SIRAgent(
                id,
                position,
                _config.InfectionProbability,
                _config.RecoveryTicks);

            _agents.Add(agent);

            _grid.Place(agent);
        }
    }

    //Infects the configured number of agents at random.
    private void InfectInitialAgents()
    {
        if (_config is null || _random is null)
            throw new InvalidOperationException();

        var selected = _agents
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

            //Small delay so dashboards can update smoothly.
            //This can later become configurable.
            await Task.Delay(50, cancellationToken);
        }

        _state = SimulationState.Completed;

        SimulationCompleted?.Invoke(this, EventArgs.Empty);
    }

    //Executes a single simulation tick.
    //Each agent is given an opportunity to perform its behaviour according to the configured scheduler.
    private void ExecuteTick()
    {
        if (_scheduler is null || _grid is null || _random is null)
            throw new InvalidOperationException("Simulation has not been initialised.");

        foreach (var agent in _scheduler.OrderAgents(_agents, _random))
        {
            //Obtain neighbouring agents from the grid.
            var neighbours = _grid.GetNeighbours(agent);

            //Allow the agent to perform one simulation step.
            agent.Step(neighbours, _random);
        }
    }

    //Calculates the current SIR population counts.
    private TickStatistics BuildStatistics()
    {
        int susceptible = 0;
        int infected = 0;
        int recovered = 0;

        foreach (var agent in _agents)
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
            Recovered = recovered
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

        _grid = null;
        _config = null;
        _scheduler = null;
        _random = null;

        _pauseTokenSource?.Dispose();
        _pauseTokenSource = null;
    }
}
