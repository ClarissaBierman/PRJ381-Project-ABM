namespace ABM.Core.Tests;

// A Random whose every roll returns the same value, so a test can force a
// probability check to pass (0.0) or fail (0.999) instead of depending on
// a seed. NextDouble() returns the value and Next(n) returns (int)(value * n).
internal sealed class FixedRandom : Random
{
    private readonly double value;

    public FixedRandom(double value)
    {
        this.value = value;
    }

    protected override double Sample() => value;

    public override double NextDouble() => value;

    public override int Next(int maxValue) => (int)(value * maxValue);

    public override int Next(int minValue, int maxValue) => minValue + (int)(value * (maxValue - minValue));
}
