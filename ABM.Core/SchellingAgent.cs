using System;
using System.Collections.Generic;
using System.Linq;

namespace ABM.Core
{
    // Schelling segregation agent. Each agent belongs to one of two groups
    // and is "happy" if at least SimilarityThreshold of its occupied
    // neighbouring cells share its own group. An unhappy agent does not
    // move itself (it has no view of the whole grid, only its immediate
    // neighbours) - it just flags itself unhappy, and the engine, which
    // owns the grid, relocates it to a random empty cell each tick.
    public class SchellingAgent : Agent
    {
        public int Group { get; }
        public bool IsHappy { get; private set; } = true;

        private readonly double similarityThreshold;

        public SchellingAgent(int id, (int X, int Y) position, int group, double similarityThreshold)
            : base(id, position)
        {
            Group = group;
            this.similarityThreshold = similarityThreshold;
        }

        public override string DisplayState => IsHappy ? $"Group{Group}-Happy" : $"Group{Group}-Unhappy";

        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            var occupiedNeighbours = neighbours.OfType<SchellingAgent>().ToList();

            if (occupiedNeighbours.Count == 0)
            {
                // No neighbours to compare against, count that as happy.
                IsHappy = true;
                return;
            }

            int sameGroup = occupiedNeighbours.Count(n => n.Group == Group);
            double similarity = (double)sameGroup / occupiedNeighbours.Count;

            IsHappy = similarity >= similarityThreshold;
        }
    }
}
