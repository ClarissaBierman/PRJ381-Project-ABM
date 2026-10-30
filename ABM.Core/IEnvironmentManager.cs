// =============================================================================
// IEnvironmentManager.cs  (ABM.Core)
//
// The "environment" an agent can see and change during its Step(). In this
// framework the environment is a 2D grid of patches (cells), implemented by
// Grid2D. Agents receive it as an interface so they can't do things only the
// engine should do, like placing or removing agents.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// What an agent is allowed to do with the grid during its turn: read and
    /// write patch data, and look up agents. Implemented by <see cref="Grid2D"/>.
    /// </summary>
    public interface IEnvironmentManager
    {
        /// <summary>Gets the patch (cell) at (x, y). Patches are created the first time they are asked for.</summary>
        Patch GetPatch(int x, int y);

        /// <summary>
        /// Gets the agents in the cells around (x, y).
        /// Moore = the 8 surrounding cells, von Neumann (moore: false) = only up, down, left, right.
        /// </summary>
        List<Agent> GetNeighbours(int x, int y, bool moore = true);

        /// <summary>Stores a value on a patch, for example grass or pheromone.</summary>
        void SetPatchProperty(int x, int y, string key, object value);

        /// <summary>Gets every agent standing on cell (x, y).</summary>
        List<Agent> GetAgentsAt(int x, int y);

        /// <summary>
        /// Turns a position that might be off the edge of the grid into a valid
        /// cell. On a Toroidal grid it wraps around to the other side. On a
        /// Bounded grid it returns null when the position is off the grid.
        /// </summary>
        (int X, int Y)? ResolvePosition((int X, int Y) position);
    }
}
