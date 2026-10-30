// =============================================================================
// Grid2D.cs  (ABM.Core)
//
// The 2D world every model runs on. It keeps track of two things:
//   1. Patches: the cells and whatever data models store on them.
//   2. Occupants: which agents are standing on which cell, so finding an
//      agent's neighbours is a quick dictionary lookup instead of a search
//      through every agent.
//
// Coordinates: (0, 0) is the top-left cell. X grows to the right, Y grows down.
//
// Topology (what happens at the edges):
//   - Toroidal: the edges wrap around, like Pac-Man. Walking off the right
//     edge brings you back on the left. This is NetLogo's default.
//   - Bounded:  the edges are walls. Positions off the grid don't exist.
// =============================================================================

namespace ABM.Core
{
    /// <summary>How the grid treats its edges.</summary>
    public enum GridTopology
    {
        /// <summary>Edges wrap around to the opposite side.</summary>
        Toroidal,

        /// <summary>Edges are walls. Off-grid positions are invalid.</summary>
        Bounded
    }

    /// <summary>
    /// The grid of patches plus a lookup of which agents stand on which cell.
    /// The engine owns it and keeps it in sync when agents move, are born or die.
    /// </summary>
    public class Grid2D : IEnvironmentManager
    {
        /// <summary>Number of columns.</summary>
        public int Width { get; }

        /// <summary>Number of rows.</summary>
        public int Height { get; }

        /// <summary>Whether the edges wrap around or act as walls.</summary>
        public GridTopology Topology { get; }

        // Patches are created lazily (the first time GetPatch asks for them),
        // so an empty 200 x 200 grid doesn't allocate 40 000 objects up front.
        private readonly Dictionary<(int X, int Y), Patch> patches = new();

        // Cell -> the agents currently standing on it. More than one agent can
        // share a cell (for example a wolf eating a sheep).
        private readonly Dictionary<(int X, int Y), List<Agent>> occupants = new();

        /// <summary>Creates an empty grid.</summary>
        /// <param name="width">Number of columns.</param>
        /// <param name="height">Number of rows.</param>
        /// <param name="topology">Edge behaviour. Wraps around by default.</param>
        public Grid2D(int width, int height, GridTopology topology = GridTopology.Toroidal)
        {
            Width = width;
            Height = height;
            Topology = topology;
        }

        /// <summary>Returns the patch at (x, y), creating it on first access.</summary>
        public Patch GetPatch(int x, int y)
        {
            var key = (x, y);
            if (!patches.TryGetValue(key, out var patch))
            {
                patch = new Patch(x, y);
                patches[key] = patch;
            }
            return patch;
        }

        /// <summary>
        /// Every patch that has been created so far (see the note on lazy
        /// creation above). That is enough for the current models: Wolf-Sheep
        /// creates every patch at setup when it plants the grass, and pheromone
        /// only ever exists on patches an ant has already touched.
        /// </summary>
        public IEnumerable<Patch> AllPatches() => patches.Values;

        /// <summary>Stores a named value on the patch at (x, y).</summary>
        public void SetPatchProperty(int x, int y, string key, object value)
        {
            GetPatch(x, y).SetProperty(key, value);
        }

        /// <summary>
        /// Adds an agent to the occupant lookup at its current position.
        /// Agents placed off a Bounded grid are ignored.
        /// </summary>
        public void Place(Agent agent)
        {
            var key = ResolvePosition(agent.Position);
            if (key == null) return;

            if (!occupants.TryGetValue(key.Value, out var list))
            {
                list = new List<Agent>();
                occupants[key.Value] = list;
            }
            list.Add(agent);
        }

        /// <summary>
        /// Moves an agent in the occupant lookup after its Position has changed.
        /// </summary>
        /// <param name="agent">The agent, already holding its new Position.</param>
        /// <param name="oldPosition">Where it stood before, so it can be removed from that cell.</param>
        public void Move(Agent agent, (int X, int Y) oldPosition)
        {
            var oldKey = ResolvePosition(oldPosition);
            if (oldKey != null && occupants.TryGetValue(oldKey.Value, out var oldList))
            {
                oldList.Remove(agent);
            }
            Place(agent);
        }

        /// <summary>Removes an agent from the occupant lookup (used when it dies).</summary>
        public void Remove(Agent agent)
        {
            var key = ResolvePosition(agent.Position);
            if (key != null && occupants.TryGetValue(key.Value, out var list))
            {
                list.Remove(agent);
            }
        }

        /// <summary>Every agent on cell (x, y). Returns an empty list for an empty or off-grid cell.</summary>
        public List<Agent> GetAgentsAt(int x, int y)
        {
            var key = ResolvePosition((x, y));
            if (key == null) return new List<Agent>();
            return occupants.TryGetValue(key.Value, out var list) ? list : new List<Agent>();
        }

        /// <summary>
        /// Picks random cells until it finds one with no agents on it. Used by
        /// Schelling, where an unhappy agent moves to any free cell.
        /// </summary>
        /// <param name="random">The run's random generator, so the result is repeatable.</param>
        /// <param name="maxAttempts">How many random cells to try before giving up.</param>
        /// <returns>An empty cell, or null if none was found in time (the grid is probably almost full).</returns>
        public (int X, int Y)? FindRandomEmptyPosition(Random random, int maxAttempts = 200)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var candidate = (X: random.Next(Width), Y: random.Next(Height));
                if (GetAgentsAt(candidate.X, candidate.Y).Count == 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// The living agents around <paramref name="agent"/>, not counting the
        /// agent itself. This is the overload the engine uses every tick, and it
        /// respects the grid's topology.
        /// </summary>
        /// <param name="agent">The agent whose neighbours we want.</param>
        /// <param name="moore">
        /// True: the 8 surrounding cells (Moore neighbourhood).
        /// False: only up, down, left and right (von Neumann neighbourhood).
        /// </param>
        public List<Agent> GetNeighbours(Agent agent, bool moore = true)
        {
            var (x, y) = agent.Position;
            var result = new List<Agent>();

            // (dx, dy) steps from the centre cell to each neighbouring cell.
            (int dx, int dy)[] mooreOffsets =
            {
                (-1, -1), (0, -1), (1, -1),
                (-1,  0),          (1,  0),
                (-1,  1), (0,  1), (1,  1)
            };

            (int dx, int dy)[] vonNeumannOffsets =
            {
                         (0, -1),
                (-1, 0),          (1, 0),
                         (0,  1)
            };

            var offsets = moore ? mooreOffsets : vonNeumannOffsets;

            foreach (var (dx, dy) in offsets)
            {
                // Off the edge of a Bounded grid there is no cell, so skip it.
                var neighbourKey = ResolvePosition((x + dx, y + dy));
                if (neighbourKey == null) continue;

                if (occupants.TryGetValue(neighbourKey.Value, out var list))
                {
                    result.AddRange(list.Where(a => a != agent && a.IsAlive));
                }
            }

            return result;
        }

        /// <summary>
        /// Turns a raw position into a valid cell: wraps it on a Toroidal grid,
        /// or returns null if it is off a Bounded grid.
        /// </summary>
        public (int X, int Y)? ResolvePosition((int X, int Y) position)
        {
            if (Topology == GridTopology.Toroidal)
            {
                // The "+ Width) % Width" part makes negative numbers wrap too:
                // in C#, -1 % 10 is -1, but we want 9.
                int wrappedX = ((position.X % Width) + Width) % Width;
                int wrappedY = ((position.Y % Height) + Height) % Height;
                return (wrappedX, wrappedY);
            }
            else
            {
                bool inBounds = position.X >= 0 && position.X < Width
                             && position.Y >= 0 && position.Y < Height;
                return inBounds ? position : null;
            }
        }

        /// <summary>
        /// Every agent in the cells around (x, y). Agents on (x, y) itself are
        /// not included, but unlike the overload above, dead agents are.
        /// <para>
        /// Known quirk: unlike the overload above, this one always wraps around
        /// the edges, even on a Bounded grid. The engine doesn't use it, but
        /// keep it in mind if you call it from a model. See docs/KNOWN-ISSUES.md.
        /// </para>
        /// </summary>
        public List<Agent> GetNeighbours(int x, int y, bool moore = true)
        {
            var result = new List<Agent>();

            (int dx, int dy)[] mooreOffsets =
            {
                (-1, -1), (0, -1), (1, -1),
                (-1,  0),          (1,  0),
                (-1,  1), (0,  1), (1,  1)
            };

            (int dx, int dy)[] vonNeumannOffsets =
            {
                         (0, -1),
                (-1, 0),          (1, 0),
                         (0,  1)
            };

            var offsets = moore ? mooreOffsets : vonNeumannOffsets;

            foreach (var (dx, dy) in offsets)
            {
                var neighbourKey = Wrap((x + dx, y + dy));
                if (occupants.TryGetValue(neighbourKey, out var list))
                {
                    result.AddRange(list);
                }
            }

            return result;
        }

        // Always wraps, whatever the topology. Only used by the overload above.
        private (int X, int Y) Wrap((int X, int Y) position)
        {
            int wrappedX = ((position.X % Width) + Width) % Width;
            int wrappedY = ((position.Y % Height) + Height) % Height;
            return (wrappedX, wrappedY);
        }
    }
}
