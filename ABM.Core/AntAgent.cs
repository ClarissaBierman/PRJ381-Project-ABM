using System;
using System.Collections.Generic;
using System.Linq;

namespace ABM.Core
{
    public enum AntState { Searching, ReturningToNest }

    // Ant foraging agent. Searches the grid for food, then lays a pheromone
    // trail back to the nest. Other ants bias their search toward stronger
    // pheromone trails, which reinforces efficient paths over time (trails
    // also decay - see SimulationEngine, which owns that global process
    // since it touches every patch, not just the ones an agent visits).
    public class AntAgent : Agent
    {
        public AntState State { get; private set; } = AntState.Searching;
        public bool CarryingFood { get; private set; }

        private readonly (int X, int Y) nestPosition;
        private readonly double pheromoneDepositAmount;
        private readonly double explorationChance;
        private (int X, int Y)? previousPosition;

        private static readonly (int dx, int dy)[] Offsets =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1,  0),          (1,  0),
            (-1,  1), (0,  1), (1,  1)
        };

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

        public override string DisplayState =>
            CarryingFood ? "ReturningWithFood" : State.ToString();

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
                // Boxed in on every side - stay put this tick.
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

            double roll = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0)
                {
                    return candidates[i];
                }
            }

            return candidates[^1];
        }

        private int DistanceToNest((int X, int Y) cell) =>
            Math.Max(Math.Abs(cell.X - nestPosition.X), Math.Abs(cell.Y - nestPosition.Y));

        private (int X, int Y) StepTowardsNest(IEnvironmentManager environment)
        {
            int dx = Math.Sign(nestPosition.X - Position.X);
            int dy = Math.Sign(nestPosition.Y - Position.Y);
            var target = (Position.X + dx, Position.Y + dy);

            // The nest itself is always a valid cell, so fall back to it
            // directly in the unlikely case the one-step target isn't.
            return environment.ResolvePosition(target) ?? nestPosition;
        }

        private void MoveTo((int X, int Y) target)
        {
            previousPosition = Position;
            Position = target;
        }
    }
}
