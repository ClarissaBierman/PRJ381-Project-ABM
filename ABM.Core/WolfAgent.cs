// =============================================================================
// WolfAgent.cs  (ABM.Core)
//
// The predator in the Wolf-Sheep predation model. Every tick a wolf:
//   1. loses some energy,
//   2. eats one random sheep on its own cell or a neighbouring cell, if there
//      is one (jumps to that sheep's cell and gains energy),
//   3. dies if its energy has run out,
//   4. maybe has a pup (splits its energy with it),
//   5. moves one cell up, down, left or right at random.
//
// With well-tuned settings (the "Stable populations" preset) both species
// survive and their numbers rise and fall in cycles.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// A wolf in the Wolf-Sheep model. Hunts nearby sheep, can starve, reproduces and moves randomly.
    /// </summary>
    public sealed class WolfAgent : Agent
    {
        private readonly double energyLossPerTick;        // energy spent just staying alive each tick
        private readonly double sheepEnergyGain;          // energy gained from eating one sheep
        private readonly double reproductionProbability;  // chance (0 to 1) of having a pup each tick

        /// <summary>Current energy. The wolf dies when this reaches 0.</summary>
        public double Energy { get; private set; }

        /// <summary>Creates a wolf.</summary>
        /// <param name="id">Unique agent id.</param>
        /// <param name="position">Starting cell.</param>
        /// <param name="energy">Starting energy.</param>
        /// <param name="energyLossPerTick">Energy lost every tick.</param>
        /// <param name="sheepEnergyGain">Energy gained when eating a sheep.</param>
        /// <param name="reproductionProbability">Chance per tick of having a pup, between 0 and 1.</param>
        public WolfAgent(
            int id,
            (int X, int Y) position,
            double energy,
            double energyLossPerTick,
            double sheepEnergyGain,
            double reproductionProbability)
            : base(id, position)
        {
            Energy = energy;
            this.energyLossPerTick = energyLossPerTick;
            this.sheepEnergyGain = sheepEnergyGain;
            this.reproductionProbability = reproductionProbability;
        }

        /// <inheritdoc />
        public override string DisplayState => "Wolf";

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            Energy -= energyLossPerTick;

            // Living sheep in the surrounding cells plus any on this wolf's own
            // cell (the neighbour list doesn't include the wolf's own cell).
            // Distinct() makes sure no sheep is counted twice.
            var sheep = neighbours.OfType<SheepAgent>()
                .Concat(environment.GetAgentsAt(Position.X, Position.Y).OfType<SheepAgent>())
                .Where(agent => agent.IsAlive)
                .Distinct()
                .ToList();
            if (sheep.Count > 0)
            {
                // Catch one at random and move onto its cell.
                var prey = sheep[rng.Next(sheep.Count)];
                prey.Die();
                Position = prey.Position;
                Energy += sheepEnergyGain;
            }

            if (Energy <= 0)
            {
                Die();
                return;
            }

            if (rng.NextDouble() < reproductionProbability)
            {
                // The parent gives half its energy to the pup. Copy the values
                // into locals because the pup is only created after the tick.
                Energy /= 2;
                var offspringEnergy = Energy;
                var position = Position;
                QueueOffspring(id => new WolfAgent(
                    id,
                    position,
                    offspringEnergy,
                    energyLossPerTick,
                    sheepEnergyGain,
                    reproductionProbability));
            }

            MoveRandomly(rng, environment);
        }

        // Steps to a random neighbouring cell (up, down, left or right) that
        // exists on the grid. Same movement rule as the sheep.
        private void MoveRandomly(Random rng, IEnvironmentManager environment)
        {
            var offsets = new (int X, int Y)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
            var candidates = offsets
                .Select(offset => environment.ResolvePosition((Position.X + offset.X, Position.Y + offset.Y)))
                .Where(position => position.HasValue)
                .Select(position => position!.Value)
                .ToList();

            if (candidates.Count > 0)
            {
                Position = candidates[rng.Next(candidates.Count)];
            }
        }
    }
}