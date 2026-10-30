// =============================================================================
// AntAgent.cs  (ABM.Core)
//
// Ant foraging, based on the ant trail research of Deneubourg et al. (1990)
// and NetLogo's "Ants" model. Each ant switches between two jobs:
//
//   Searching:       wander around looking for food. Cells with pheromone
//                    (scent) that lead away from the nest are more likely to
//                    be picked, so ants tend to follow existing trails outwards.
//   ReturningToNest: carry one piece of food straight home, dropping
//                    pheromone on every cell along the way.
//
// Because good trails get reinforced by every ant that uses them, and unused
// trails slowly fade, the colony "finds" short paths to food without any ant
// knowing the map. The fading (decay) happens in SimulationEngine, because it
// has to touch every patch, not only the ones an ant is standing on.
//
// Patch values this model uses: "food" (int), "pheromone" (double), "nest" (bool).
// =============================================================================

namespace ABM.Core
{
    /// <summary>What an ant is currently doing.</summary>
    public enum AntState { Searching, ReturningToNest }

    /// <summary>
    /// An ant that searches for food and lays pheromone trails back to the nest.
    /// </summary>
    public class AntAgent : Agent
    {
        /// <summary>Current job: searching for food, or carrying it home.</summary>
        public AntState State { get; private set; } = AntState.Searching;

        /// <summary>True while the ant is carrying a piece of food.</summary>
        public bool CarryingFood { get; private set; }

        private readonly (int X, int Y) nestPosition;
        private readonly double pheromoneDepositAmount;  // scent dropped per cell on the way home
        private readonly double explorationChance;       // chance (0 to 1) of ignoring scent and moving randomly

        // The cell the ant came from, so it doesn't step straight back.
        private (int X, int Y)? previousPosition;

        // The 8 directions an ant can step in (Moore neighbourhood).
        private static readonly (int dx, int dy)[] Offsets =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1,  0),          (1,  0),
            (-1,  1), (0,  1), (1,  1)
        };

        /// <summary>Creates an ant that starts out searching.</summary>
        /// <param name="id">Unique agent id.</param>
        /// <param name="position">Starting cell (normally the nest).</param>
        /// <param name="nestPosition">Where the nest is, so the ant can find its way home.</param>
        /// <param name="pheromoneDepositAmount">Scent dropped on each cell while carrying food home.</param>
        /// <param name="explorationChance">Chance per step of moving randomly instead of following scent.</param>
        public AntAgent(
            int id,
            (int X, int Y) position,
            (int X, int Y) nestPosition,
            double pheromoneDepositAmount = 5.0,
            double explorationChance = 0.1)
            : base(id, position)
        {
            this.nestPosition = nestPosition;
            this.pheromoneDepositAmount = pheromoneDepositAmount;
            this.explorationChance = explorationChance;
        }

        /// <inheritdoc />
        public override string DisplayState =>
            CarryingFood ? "ReturningWithFood" : State.ToString();

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            switch (State)
            {
                case AntState.Searching:
                    Search(rng, environment);
                    break;

                case AntState.ReturningToNest:
                    ReturnToNest(environment);
                    break;
            }
        }

        // Picks up food if there is some on this cell, otherwise takes one search step.
        private void Search(Random rng, IEnvironmentManager environment)
        {
            var patch = environment.GetPatch(Position.X, Position.Y);
            var food = patch.GetProperty("food") is int f ? f : 0;

            if (food > 0)
            {
                patch.SetProperty("food", food - 1);
                CarryingFood = true;
                State = AntState.ReturningToNest;
                previousPosition = null;
                return;
            }

            MoveTo(ChooseSearchStep(rng, environment));
        }

        // Drops the food off at the nest, or lays scent and takes one step home.
        private void ReturnToNest(IEnvironmentManager environment)
        {
            if (Position == nestPosition)
            {
                CarryingFood = false;
                State = AntState.Searching;
                previousPosition = null;
                return;
            }

            // Lay a pheromone trail on the way back, so other ants can follow it.
            var patch = environment.GetPatch(Position.X, Position.Y);
            var current = patch.GetProperty("pheromone") is double p ? p : 0.0;
            patch.SetProperty("pheromone", current + pheromoneDepositAmount);

            MoveTo(StepTowardsNest(environment));
        }

        // Chooses the next cell while searching. Mostly a weighted random pick
        // that favours scent leading away from the nest, sometimes fully random.
        private (int X, int Y) ChooseSearchStep(Random rng, IEnvironmentManager environment)
        {
            // Resolve every candidate through the environment first, so a
            // Bounded ant never steps off the grid (where it would be stuck
            // forever, since nothing pulls an out-of-range position back),
            // and a Toroidal ant wraps around the edge like everything else.
            var candidates = Offsets
                .Select(o => (X: Position.X + o.dx, Y: Position.Y + o.dy))
                .Select(c => environment.ResolvePosition(c))
                .Where(c => c != null)
                .Select(c => c!.Value)
                .ToList();

            if (candidates.Count == 0)
            {
                // Boxed in on every side, so stay put this tick.
                return Position;
            }

            // Don't step straight back to the previous cell, so ants keep
            // moving along a trail instead of bouncing on the spot.
            var forward = candidates.Where(c => c != previousPosition).ToList();
            if (forward.Count > 0)
            {
                candidates = forward;
            }

            if (rng.NextDouble() < explorationChance)
            {
                return candidates[rng.Next(candidates.Count)];
            }

            // Weighted random choice: pheromone only attracts the ant to cells
            // leading away from the nest, so a searching ant follows a trail
            // out toward the food rather than back home. Every cell keeps a
            // base weight of 1 so ants still explore off the trail.
            int currentDistance = DistanceToNest(Position);
            var weights = candidates
                .Select(c =>
                {
                    var pheromone = environment.GetPatch(c.X, c.Y).GetProperty("pheromone") is double v ? v : 0.0;
                    bool outward = DistanceToNest(c) >= currentDistance;
                    return 1.0 + (outward ? pheromone : 0.0);
                })
                .ToList();

            // "Roulette wheel" pick: roll a number between 0 and the total
            // weight, then walk through the cells subtracting each weight until
            // the roll runs out. Heavier cells take up more of the wheel.
            double roll = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0)
                {
                    return candidates[i];
                }
            }

            // Only reached through floating-point rounding. Take the last cell.
            return candidates[^1];
        }

        // Distance in steps when diagonal moves are allowed (Chebyshev distance).
        private int DistanceToNest((int X, int Y) cell) =>
            Math.Max(Math.Abs(cell.X - nestPosition.X), Math.Abs(cell.Y - nestPosition.Y));

        // One step straight towards the nest: Math.Sign gives -1, 0 or +1 for
        // each direction, which is a move to one of the 8 neighbouring cells.
        private (int X, int Y) StepTowardsNest(IEnvironmentManager environment)
        {
            int dx = Math.Sign(nestPosition.X - Position.X);
            int dy = Math.Sign(nestPosition.Y - Position.Y);
            var target = (Position.X + dx, Position.Y + dy);

            // The nest itself is always a valid cell, so fall back to it
            // directly in the unlikely case the one-step target isn't.
            return environment.ResolvePosition(target) ?? nestPosition;
        }

        // Moves the ant and remembers where it came from.
        private void MoveTo((int X, int Y) target)
        {
            previousPosition = Position;
            Position = target;
        }
    }
}
