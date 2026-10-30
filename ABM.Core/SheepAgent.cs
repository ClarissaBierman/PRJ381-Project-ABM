// =============================================================================
// SheepAgent.cs  (ABM.Core)
//
// The prey in the Wolf-Sheep predation model (based on Uri Wilensky's NetLogo
// model, 1997). Every tick a sheep:
//   1. loses some energy,
//   2. eats the grass on its cell if there is any (gains energy, grass is gone),
//   3. dies if its energy has run out,
//   4. maybe has a lamb (splits its energy with it),
//   5. moves one cell up, down, left or right at random.
//
// Grass regrowth is handled by SimulationEngine, since it affects every patch.
// Wolves eat sheep, see WolfAgent.cs.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// A sheep in the Wolf-Sheep model. Eats grass, can starve, reproduces and moves randomly.
    /// </summary>
    public sealed class SheepAgent : Agent
    {
        private readonly double energyLossPerTick;        // energy spent just staying alive each tick
        private readonly double grassEnergyGain;          // energy gained from eating one cell of grass
        private readonly double reproductionProbability;  // chance (0 to 1) of having a lamb each tick

        /// <summary>Current energy. The sheep dies when this reaches 0.</summary>
        public double Energy { get; private set; }

        /// <summary>Creates a sheep.</summary>
        /// <param name="id">Unique agent id.</param>
        /// <param name="position">Starting cell.</param>
        /// <param name="energy">Starting energy.</param>
        /// <param name="energyLossPerTick">Energy lost every tick.</param>
        /// <param name="grassEnergyGain">Energy gained when eating grass.</param>
        /// <param name="reproductionProbability">Chance per tick of having a lamb, between 0 and 1.</param>
        public SheepAgent(
            int id,
            (int X, int Y) position,
            double energy,
            double energyLossPerTick,
            double grassEnergyGain,
            double reproductionProbability)
            : base(id, position)
        {
            Energy = energy;
            this.energyLossPerTick = energyLossPerTick;
            this.grassEnergyGain = grassEnergyGain;
            this.reproductionProbability = reproductionProbability;
        }

        /// <inheritdoc />
        public override string DisplayState => "Sheep";

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            Energy -= energyLossPerTick;

            // Eat the grass on this cell, if there is any.
            var patch = environment.GetPatch(Position.X, Position.Y);
            if (patch.GetProperty("grass") is true)
            {
                patch.SetProperty("grass", false);
                Energy += grassEnergyGain;
            }

            if (Energy <= 0)
            {
                Die();
                return;
            }

            if (rng.NextDouble() < reproductionProbability)
            {
                // The parent gives half its energy to the lamb.
                Energy /= 2;

                // Copy these into locals first: the lamb is only created after
                // the tick, and by then this sheep may have moved or changed.
                var offspringEnergy = Energy;
                var position = Position;
                QueueOffspring(id => new SheepAgent(
                    id,
                    position,
                    offspringEnergy,
                    energyLossPerTick,
                    grassEnergyGain,
                    reproductionProbability));
            }

            MoveRandomly(rng, environment);
        }

        // Steps to a random neighbouring cell (up, down, left or right) that
        // exists on the grid.
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