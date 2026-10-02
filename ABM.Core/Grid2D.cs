namespace ABM.Core
{
    public enum GridTopology { Toroidal, Bounded }

    public class Grid2D : IEnvironmentManager
    {
        public int Width { get; }
        public int Height { get; }
        public GridTopology Topology { get; }

        private readonly Dictionary<(int X, int Y), Patch> patches = new();
        private readonly Dictionary<(int X, int Y), List<Agent>> occupants = new();

        public Grid2D(int width, int height, GridTopology topology = GridTopology.Toroidal)
        {
            Width = width;
            Height = height;
            Topology = topology;
        }

        // Returns the Patch at (x, y), creating it on first access.
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

        // Returns every patch that has been accessed so far. Used by models
        // that need to iterate the whole environment, such as decaying
        // pheromone trails each tick for Ant Foraging.
        public IEnumerable<Patch> AllPatches() => patches.Values;

        public void SetPatchProperty(int x, int y, string key, object value)
        {
            GetPatch(x, y).SetProperty(key, value);
        }

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

        public void Move(Agent agent, (int X, int Y) oldPosition)
        {
            var oldKey = ResolvePosition(oldPosition);
            if (oldKey != null && occupants.TryGetValue(oldKey.Value, out var oldList))
            {
                oldList.Remove(agent);
            }
            Place(agent);
        }

        public List<Agent> GetAgentsAt(int x, int y)
        {
            var key = ResolvePosition((x, y));
            if (key == null) return new List<Agent>();
            return occupants.TryGetValue(key.Value, out var list) ? list : new List<Agent>();
        }

        // Returns every grid cell currently holding zero agents, scanning the
        // full Width x Height range. Used by models (e.g. Schelling) where an
        // unhappy agent needs to relocate to any free cell.
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

        public List<Agent> GetNeighbours(Agent agent, bool moore = true)
        {
            var (x, y) = agent.Position;
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
                var neighbourKey = ResolvePosition((x + dx, y + dy));
                if (neighbourKey == null) continue;

                if (occupants.TryGetValue(neighbourKey.Value, out var list))
                {
                    result.AddRange(list.Where(a => a != agent));
                }
            }

            return result;
        }

        // Converts a raw (possibly out-of-range) position into a valid grid key.
        public (int X, int Y)? ResolvePosition((int X, int Y) position)
        {
            if (Topology == GridTopology.Toroidal)
            {
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

        private (int X, int Y) Wrap((int X, int Y) position)
        {
            int wrappedX = ((position.X % Width) + Width) % Width;
            int wrappedY = ((position.Y % Height) + Height) % Height;
            return (wrappedX, wrappedY);
        }
    }
}
