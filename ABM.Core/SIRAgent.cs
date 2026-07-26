using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Core
{
    public enum HealthState { Susceptible, Infected, Recovered }

    public class SIRAgent : Agent
    {
        public HealthState State { get; private set; }
        private int ticksInfected;

        private readonly double infectionProbability;
        private readonly int recoveryTime;

        public SIRAgent(int id, (int X, int Y) position, double infectionProbability, int recoveryTime)
            : base(id, position)
        {
            State = HealthState.Susceptible;
            this.infectionProbability = infectionProbability;
            this.recoveryTime = recoveryTime;
        }

        public void Infect() => State = HealthState.Infected;

        public override void Step(IReadOnlyList<Agent> neighbours, Random rng)
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
                    // Permanent - nothing to do
                    break;
            }
        }

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
