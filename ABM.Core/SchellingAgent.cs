// =============================================================================
// SchellingAgent.cs  (ABM.Core)
//
// Schelling's segregation model (Thomas Schelling, 1971). Agents belong to one
// of two groups. An agent is happy if at least SimilarityThreshold (for
// example 30%) of its neighbours are in the same group as itself. Unhappy
// agents move to a random empty cell.
//
// The interesting result: even a mild preference (most agents are fine being a
// minority) still ends in strongly separated neighbourhoods.
//
// Note on who moves the agent: the agent only knows about its neighbours, not
// the whole grid, so it can't find an empty cell itself. It just sets IsHappy,
// and the engine (which owns the grid) moves every unhappy agent after the tick.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// An agent in the Schelling segregation model. Decides each tick whether it
    /// is happy with its neighbours. The engine moves the unhappy ones.
    /// </summary>
    public class SchellingAgent : Agent
    {
        /// <summary>Which group the agent belongs to (the dashboard shows them as red and blue).</summary>
        public int Group { get; }

        /// <summary>True if enough neighbours are in the same group. Worked out again every tick.</summary>
        public bool IsHappy { get; private set; } = true;

        // Fraction (0 to 1) of neighbours that must share the agent's group.
        private readonly double similarityThreshold;

        /// <summary>Creates a Schelling agent.</summary>
        /// <param name="id">Unique agent id.</param>
        /// <param name="position">Starting grid cell.</param>
        /// <param name="group">The agent's group number.</param>
        /// <param name="similarityThreshold">Fraction of same-group neighbours needed to be happy, between 0 and 1.</param>
        public SchellingAgent(int id, (int X, int Y) position, int group, double similarityThreshold)
            : base(id, position)
        {
            Group = group;
            this.similarityThreshold = similarityThreshold;
        }

        /// <inheritdoc />
        public override string DisplayState => IsHappy ? $"Group{Group}-Happy" : $"Group{Group}-Unhappy";

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            var occupiedNeighbours = neighbours.OfType<SchellingAgent>().ToList();

            if (occupiedNeighbours.Count == 0)
            {
                // No neighbours to compare against, so count that as happy.
                IsHappy = true;
                return;
            }

            int sameGroup = occupiedNeighbours.Count(n => n.Group == Group);
            double similarity = (double)sameGroup / occupiedNeighbours.Count;

            IsHappy = similarity >= similarityThreshold;
        }
    }
}
