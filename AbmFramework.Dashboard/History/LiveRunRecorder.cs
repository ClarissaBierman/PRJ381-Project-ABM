// =============================================================================
// LiveRunRecorder.cs  (AbmFramework.Dashboard.History)
//
// Saves ONE live dashboard run to the database while it runs. One recorder is
// made per run by LiveSimulationService.
//
// What gets saved:
//   - the run itself and its grid settings (straight away)
//   - every tick's statistics
//   - every agent that ever existed, with its first and last tick
//   - agent positions and states: at Setup (tick -1), every _snapshotEvery
//     ticks (at least 10, more for big populations) and at the end
//   - patch data (grass, food, nest, pheromone): at Setup and at the end
//
// Speed: OnTick runs on the engine's thread, so it only copies a few values
// into lists. Every TicksPerWrite ticks (or on pause / finish) the lists are
// handed to HistoryWriter, which writes them on its own background thread.
//
// Threads: the engine thread calls OnTick while a web request might call Flush
// or Finish, so every public method locks _gate.
// =============================================================================

using System.Text.Json;
using ABM.Core;
using ABM.Persistence.Models;
using AbmFramework.Config;
using AbmFramework.Engine;

namespace AbmFramework.Dashboard.History;

/// <summary>
/// Saves one live dashboard run as it happens. Ticks are collected in memory
/// and handed to the HistoryWriter in chunks, so the engine thread only ever
/// copies a few values.
/// </summary>
public sealed class LiveRunRecorder
{
    /// <summary>How many ticks to collect before handing them to the database writer.</summary>
    public const int TicksPerWrite = 25;

    private readonly HistoryWriter _writer;
    private readonly object _gate = new();
    private readonly Dictionary<int, AgentRecord> _agents = new();   // every agent seen so far, by id
    private readonly HashSet<int> _changedAgents = new();             // agents new since the last write
    private List<TickRecord> _ticks = new();                          // ticks waiting to be written
    private List<AgentState> _states = new();                         // agent states waiting to be written
    private readonly int _snapshotEvery;                              // save agent states every this many ticks
    private int _lastTick = -1;                                       // the latest tick seen
    private int _lastSnapshotTick = int.MinValue;                     // the tick of the latest agent-state capture
    private bool _finished;                                           // after Finish, everything is ignored

    // When a saved run is continued, its first ticks are replayed to get back
    // to where it stopped. Those ticks are already in the database.
    private readonly int _alreadySaved;

    /// <summary>The run's id in the database.</summary>
    public Guid SimulationId { get; }

    /// <summary>The run's grid (EnvironmentRecord) id in the database.</summary>
    public Guid EnvironmentId { get; }

    /// <summary>Starts recording a brand new run (saves the run and its grid straight away).</summary>
    public LiveRunRecorder(HistoryWriter writer, SimulationConfig config)
        : this(writer, config, Guid.NewGuid(), Guid.NewGuid(), 0)
    {
    }

    /// <summary>
    /// Carries on saving into a run that is already in the database (Continue).
    /// The first savedTicks ticks are replayed but not saved again.
    /// </summary>
    public static LiveRunRecorder Continue(HistoryWriter writer, SimulationConfig config, Guid simulationId, Guid environmentId, int savedTicks) =>
        new(writer, config, simulationId, environmentId, savedTicks);

    private LiveRunRecorder(HistoryWriter writer, SimulationConfig config, Guid simulationId, Guid environmentId, int savedTicks)
    {
        _writer = writer;
        SimulationId = simulationId;
        EnvironmentId = environmentId;
        _alreadySaved = savedTicks;

        // Big populations make big snapshots, so save them less often:
        // every 10 ticks, or every (agents / 100) ticks if that is more.
        int agents = config.Model == ModelType.WolfSheep ? config.InitialSheep + config.InitialWolves : config.AgentCount;
        _snapshotEvery = Math.Max(10, agents / 100);

        if (savedTicks > 0)
        {
            _writer.Enqueue(repository => repository.UpdateSimulationAsync(SimulationId, "Running", null, savedTicks));
            return;
        }

        var simulation = new Simulation
        {
            Id = SimulationId,
            Model = config.Model.ToString(),
            ScenarioName = config.ScenarioName,
            Source = "Live",
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            Seed = config.RandomSeed,
            ConfigurationJson = JsonSerializer.Serialize(config, HistoryWriter.Json)
        };

        var environment = new EnvironmentRecord
        {
            Id = EnvironmentId,
            SimulationId = SimulationId,
            GridWidth = config.GridWidth,
            GridHeight = config.GridHeight,
            Topology = config.Topology.ToString(),
            Scheduler = config.Scheduler.ToString()
        };

        _writer.Enqueue(async repository =>
        {
            await repository.SaveSimulationAsync(simulation);
            await repository.SaveEnvironmentAsync(environment);
        });
    }

    /// <summary>Called once from Setup, with the agents and patches before any tick.</summary>
    public void OnInitialised(SimulationSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_finished) return;

            TrackAgents(snapshot.Agents, -1);
            if (_alreadySaved > 0) return;

            CaptureStates(snapshot.Agents, -1);
            var patches = CapturePatches(snapshot.Grid, -1);
            WriteBuffered(patches);
        }
    }

    /// <summary>Called after every tick (on the engine's thread). Collects the tick and maybe a snapshot.</summary>
    public void OnTick(TickStatistics stats, SimulationSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_finished) return;

            _lastTick = stats.Tick;
            TrackAgents(snapshot.Agents, stats.Tick);
            // Still replaying ticks that are already in the database.
            if (stats.Tick < _alreadySaved) return;

            _ticks.Add(ToRecord(SimulationId, stats));

            if ((stats.Tick + 1) % _snapshotEvery == 0)
            {
                CaptureStates(snapshot.Agents, stats.Tick);
            }

            if (_ticks.Count >= TicksPerWrite)
            {
                WriteBuffered(null);
            }
        }
    }

    /// <summary>Writes whatever is waiting, e.g. when the run is paused.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_finished) WriteBuffered(null);
        }
    }

    /// <summary>
    /// Saves the end of the run and marks it finished. Safe to call more than
    /// once: only the first call counts.
    /// </summary>
    /// <param name="status">"Completed", "Stopped" or "Error".</param>
    /// <param name="snapshot">The final state, to save end positions and patches. Can be null.</param>
    public void Finish(string status, SimulationSnapshot? snapshot)
    {
        lock (_gate)
        {
            if (_finished) return;

            // Nothing new to keep: a run that never got past Setup is removed,
            // and a continued run stopped while replaying keeps what it had.
            if (_lastTick < _alreadySaved)
            {
                _finished = true;
                if (_alreadySaved == 0)
                {
                    _writer.Enqueue(repository => repository.DeleteSimulationAsync(SimulationId));
                }
                else
                {
                    var stoppedAt = DateTime.UtcNow;
                    _writer.Enqueue(repository => repository.UpdateSimulationAsync(SimulationId, "Stopped", stoppedAt, _alreadySaved));
                }
                return;
            }

            List<PatchRecord>? patches = null;
            if (snapshot is not null)
            {
                TrackAgents(snapshot.Agents, _lastTick);
                if (_lastSnapshotTick != _lastTick)
                {
                    CaptureStates(snapshot.Agents, _lastTick);
                }
                patches = CapturePatches(snapshot.Grid, _lastTick);
            }

            // Every agent's last tick is only known now, e.g. when each sheep was eaten.
            foreach (var id in _agents.Keys)
            {
                _changedAgents.Add(id);
            }

            WriteBuffered(patches);
            _finished = true;

            int tickCount = _lastTick + 1;
            var ended = DateTime.UtcNow;
            _writer.Enqueue(repository => repository.UpdateSimulationAsync(SimulationId, status, ended, tickCount));
        }
    }

    // Hands everything collected so far to the HistoryWriter as ONE queued job,
    // and starts new empty lists. The old lists now belong to that job, so the
    // engine thread can keep filling the new ones safely.
    private void WriteBuffered(List<PatchRecord>? patches)
    {
        var ticks = _ticks;
        var states = _states;
        var agents = _changedAgents.Select(id => Copy(_agents[id])).ToList();
        _ticks = new List<TickRecord>();
        _states = new List<AgentState>();
        _changedAgents.Clear();

        int tickCount = _lastTick + 1;
        if (ticks.Count == 0 && states.Count == 0 && agents.Count == 0 && patches is null) return;

        _writer.Enqueue(async repository =>
        {
            await repository.SaveAgentsAsync(agents);
            await repository.SaveTickRecordsAsync(ticks);
            await repository.SaveAgentStatesAsync(states);
            if (patches is not null) await repository.SavePatchesAsync(patches);
            if (ticks.Count > 0) await repository.UpdateSimulationAsync(SimulationId, "Running", null, tickCount);
        });
    }

    // Records any agent not seen before, and moves every agent's LastTick forward.
    private void TrackAgents(IReadOnlyList<Agent> agents, int tick)
    {
        foreach (var agent in agents)
        {
            if (!_agents.TryGetValue(agent.Id, out var record))
            {
                record = new AgentRecord
                {
                    SimulationId = SimulationId,
                    AgentId = agent.Id,
                    Breed = Breed(agent),
                    FirstTick = tick
                };
                _agents[agent.Id] = record;
                _changedAgents.Add(agent.Id);
            }

            record.LastTick = tick;
        }
    }

    // Saves every agent's position, state and extra properties at this tick.
    private void CaptureStates(IReadOnlyList<Agent> agents, int tick)
    {
        _lastSnapshotTick = tick;
        foreach (var agent in agents)
        {
            _states.Add(new AgentState
            {
                SimulationId = SimulationId,
                TickNumber = tick,
                AgentId = agent.Id,
                X = agent.Position.X,
                Y = agent.Position.Y,
                State = agent.DisplayState,
                PropertiesJson = Properties(agent)
            });
        }
    }

    // Saves every patch that has grass, food, nest or pheromone on it.
    private List<PatchRecord> CapturePatches(Grid2D grid, int tick)
    {
        var patches = new List<PatchRecord>();
        foreach (var patch in grid.AllPatches())
        {
            var values = new Dictionary<string, object>();
            foreach (var key in new[] { "grass", "food", "nest", "pheromone" })
            {
                if (patch.GetProperty(key) is { } value)
                {
                    values[key] = value is double d ? Math.Round(d, 3) : value;
                }
            }

            if (values.Count == 0) continue;

            patches.Add(new PatchRecord
            {
                EnvironmentId = EnvironmentId,
                TickNumber = tick,
                X = patch.X,
                Y = patch.Y,
                PropertiesJson = JsonSerializer.Serialize(values)
            });
        }
        return patches;
    }

    /// <summary>Turns the engine's TickStatistics into a database TickRecord.</summary>
    public static TickRecord ToRecord(Guid simulationId, TickStatistics stats) => new()
    {
        SimulationId = simulationId,
        TickNumber = stats.Tick,
        Susceptible = stats.Susceptible,
        Infected = stats.Infected,
        Recovered = stats.Recovered,
        TotalPopulation = stats.TotalPopulation,
        Metrics = stats.Metrics
    };

    // A copy, so the queued write isn't affected by later LastTick changes.
    private static AgentRecord Copy(AgentRecord a) => new()
    {
        SimulationId = a.SimulationId,
        AgentId = a.AgentId,
        Breed = a.Breed,
        FirstTick = a.FirstTick,
        LastTick = a.LastTick
    };

    // "SheepAgent" -> "Sheep".
    private static string Breed(Agent agent)
    {
        var name = agent.GetType().Name;
        return name.EndsWith("Agent") ? name[..^"Agent".Length] : name;
    }

    // Extra per-breed values worth saving, as JSON. Null for breeds without any.
    private static string? Properties(Agent agent) => agent switch
    {
        SheepAgent sheep => JsonSerializer.Serialize(new { energy = Math.Round(sheep.Energy, 2) }),
        WolfAgent wolf => JsonSerializer.Serialize(new { energy = Math.Round(wolf.Energy, 2) }),
        BoidAgent boid => JsonSerializer.Serialize(new
        {
            heading = Math.Round(Math.Atan2(boid.VY, boid.VX), 3),
            speed = Math.Round(Math.Sqrt(boid.VX * boid.VX + boid.VY * boid.VY), 3)
        }),
        AntAgent ant => JsonSerializer.Serialize(new { carryingFood = ant.CarryingFood }),
        _ => null
    };
}
