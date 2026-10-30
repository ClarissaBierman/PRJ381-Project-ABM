// =============================================================================
// SIRAgent.cs  (ABM.Core)
//
// The SIR epidemic model (Kermack and McKendrick, 1927). Every agent is a
// person in one of three states:
//   S - Susceptible: healthy, can catch the infection.
//   I - Infected:    sick, can pass it on to neighbours.
//   R - Recovered:   immune for good.
//
// Each tick a susceptible person gets one chance per infected neighbour to
// catch the infection. An infected person recovers after a fixed number of
// ticks. Plotting S, I and R over time gives the classic epidemic curve.
//
// People in this model don't move. The engine sets up the population and
// infects the first few people (InitialInfected in SimulationConfig).
// =============================================================================

namespace ABM.Core
{
    /// <summary>The three states of the SIR model.</summary>
    public enum HealthState { Susceptible, Infected, Recovered }

    /// <summary>
    /// A person in the SIR epidemic model. Catches the infection from infected
    /// neighbours and recovers after <c>recoveryTime</c> ticks.
    /// </summary>
    public class SIRAgent : Agent
    {
        /// <summary>The person's current health state.</summary>
        public HealthState State { get; private set; }

        // How many ticks this person has been sick for.
        private int ticksInfected;

        // Chance (0 to 1) of catching it from EACH infected neighbour per tick.
        private readonly double infectionProbability;

        // How many ticks someone stays infected before recovering.
        private readonly int recoveryTime;

        /// <summary>Creates a healthy (susceptible) person.</summary>
        /// <param name="id">Unique agent id.</param>
        /// <param name="position">Grid cell the person lives on.</param>
        /// <param name="infectionProbability">Chance per infected neighbour per tick, between 0 and 1.</param>
        /// <param name="recoveryTime">Ticks spent infected before recovering.</param>
        public SIRAgent(int id, (int X, int Y) position, double infectionProbability, int recoveryTime)
            : base(id, position)
        {
            State = HealthState.Susceptible;
            this.infectionProbability = infectionProbability;
            this.recoveryTime = recoveryTime;
        }

        /// <summary>Makes this person infected straight away. The engine uses it for the first cases ("patient zero").</summary>
        public void Infect() => State = HealthState.Infected;

        /// <inheritdoc />
        public override string DisplayState => State.ToString();

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            switch (State)
            {
                case HealthState.Susceptible:
                    TryGetInfected(neighbours, rng);
                    break;

                case HealthState.Infected:
                    ticksInfected++;
                    if (ticksInfected >= recoveryTime)
                    {
                        State = HealthState.Recovered;
                    }
                    break;

                case HealthState.Recovered:
                    // Recovered people are immune for good, so nothing happens.
                    break;
            }
        }

        // Rolls the dice once for every infected neighbour. The first success
        // infects this person and stops, so more sick neighbours means a higher
        // overall chance of getting sick this tick.
        private void TryGetInfected(IReadOnlyList<Agent> neighbours, Random rng)
        {
            foreach (var neighbour in neighbours)
            {
                if (neighbour is SIRAgent sirNeighbour && sirNeighbour.State == HealthState.Infected)
                {
                    if (rng.NextDouble() < infectionProbability)
                    {
                        State = HealthState.Infected;
                        ticksInfected = 0;
                        return;
                    }
                }
            }
        }
    }
}
