// =============================================================================
// Patch.cs  (ABM.Core)
//
// One cell of the grid. NetLogo calls these "patches", so we kept the name.
// A patch can hold any number of named values, which is how models keep state
// in the environment instead of in the agents:
//   - Wolf-Sheep stores "grass" (true = grass, false = eaten).
//   - Ant Foraging stores "pheromone", "food" and "nest".
// Values are stored as object, so read them with pattern matching,
// for example: if (patch.GetProperty("grass") is true) { ... }
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// A single grid cell with a free-form bag of named properties.
    /// </summary>
    public class Patch
    {
        /// <summary>Column of this cell (0 is the left edge).</summary>
        public int X { get; }

        /// <summary>Row of this cell (0 is the top edge).</summary>
        public int Y { get; }

        // The named values stored on this cell, for example "grass" -> true.
        private readonly Dictionary<string, object> properties = new();

        /// <summary>Creates an empty patch at (x, y). Normally only <see cref="Grid2D"/> does this.</summary>
        public Patch(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Sets (or overwrites) a named value on this patch.</summary>
        public void SetProperty(string key, object value)
        {
            properties[key] = value;
        }

        /// <summary>Gets a named value, or null if it was never set.</summary>
        public object? GetProperty(string key)
        {
            return properties.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>True if a value with this name has been set on the patch.</summary>
        public bool HasProperty(string key) => properties.ContainsKey(key);
    }
}