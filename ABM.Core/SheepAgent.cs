namespace ABM.Core
{
    public sealed class SheepAgent : Agent
    {
        private readonly double energyLossPerTick;
        private readonly double grassEnergyGain;
        private readonly double reproductionProbability;

        public double Energy { get; private set; }

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

        public override string DisplayState => "Sheep";

        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            Energy -= energyLossPerTick;

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
                Energy /= 2;
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