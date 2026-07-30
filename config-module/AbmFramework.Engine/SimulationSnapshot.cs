using ABM.Core;
using AbmFramework.Config;

namespace AbmFramework.Engine;

public sealed class SimulationSnapshot
{
    //Configuration used to initialise the simulation.
    public required SimulationConfig Configuration { get; init; }

    //Current environment grid.
    public required Grid2D Grid { get; init; }

    //All agents currently participating in the simulation.
    public required IReadOnlyList<SIRAgent> Agents { get; init; }

    //Latest simulation statistics.
    public required TickStatistics Statistics { get; init; }

    //Current simulation state.
    public SimulationState State { get; init; }

    //Current simulation tick.
    public int Tick => Statistics.Tick;
}