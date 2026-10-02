namespace ABM.Core
{
    public interface IEnvironmentManager
    {
        Patch GetPatch(int x, int y);
        List<Agent> GetNeighbours(int x, int y, bool moore = true);
        void SetPatchProperty(int x, int y, string key, object value);
        List<Agent> GetAgentsAt(int x, int y);

        // Converts a raw (possibly out-of-range) position into a valid grid
        // position, applying the grid's topology (wrapping for Toroidal,
        // or returning null for an out-of-bounds Bounded position).
        (int X, int Y)? ResolvePosition((int X, int Y) position);
    }
}
