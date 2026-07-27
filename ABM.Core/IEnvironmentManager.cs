namespace ABM.Core
{
    public interface IEnvironmentManager
    {
        Patch GetPatch(int x, int y);
        List<Agent> GetNeighbours(int x, int y, bool moore = true);
        void SetPatchProperty(int x, int y, string key, object value);
        List<Agent> GetAgentsAt(int x, int y);
    }
}