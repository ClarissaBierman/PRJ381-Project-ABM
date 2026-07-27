namespace ABM.Core
{
    public class Patch
    {
        public int X { get; }
        public int Y { get; }

        private readonly Dictionary<string, object> properties = new();

        public Patch(int x, int y)
        {
            X = x;
            Y = y;
        }

        public void SetProperty(string key, object value)
        {
            properties[key] = value;
        }

        public object? GetProperty(string key)
        {
            return properties.TryGetValue(key, out var value) ? value : null;
        }

        public bool HasProperty(string key) => properties.ContainsKey(key);
    }
}