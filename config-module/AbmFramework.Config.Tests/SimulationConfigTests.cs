// =============================================================================
// SimulationConfigTests.cs  (AbmFramework.Config.Tests)
//
// Unit tests for SimulationConfig, mainly Clone(): the copy must have every
// value and be fully independent of the original.
//
// How to run every test: in Visual Studio open Test > Test Explorer and click
// "Run All Tests", or run "dotnet test" in the solution folder.
// Tests use xUnit: [Fact] is one test, [Theory] + [InlineData] runs the same
// test once per data row. Most tests follow Arrange (set up), Act (do the
// thing), Assert (check the result).
// =============================================================================

using System.Reflection;
using AbmFramework.Config;
using Xunit;

namespace AbmFramework.Config.Tests;

/// <summary>Tests for SimulationConfig.</summary>
public class SimulationConfigTests
{
    private static readonly PropertyInfo[] Properties =
        typeof(SimulationConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    // Gives every property a value that differs from its default, whatever
    // its type, so the test covers properties added in the future too.
    private static SimulationConfig WithEveryPropertyChanged()
    {
        var config = new SimulationConfig();
        foreach (var property in Properties)
        {
            object value = property.PropertyType switch
            {
                var t when t == typeof(string) => "Changed " + property.Name,
                var t when t == typeof(int) => (int)property.GetValue(config)! + 7,
                var t when t == typeof(int?) => 1234,
                var t when t == typeof(double) => (double)property.GetValue(config)! + 0.123,
                var t when t.IsEnum => Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)!,
                var t => throw new NotSupportedException(
                    $"Add a test value for {t.Name} properties such as {property.Name}.")
            };
            property.SetValue(config, value);
        }
        return config;
    }

    [Fact]
    public void Clone_CopiesEveryProperty()
    {
        var original = WithEveryPropertyChanged();

        var clone = original.Clone();

        Assert.All(Properties, p => Assert.Equal(p.GetValue(original), p.GetValue(clone)));
    }

    [Fact]
    public void Clone_IsIndependentOfTheOriginal()
    {
        var original = new SimulationConfig { RandomSeed = 1, AgentCount = 10 };

        var clone = original.Clone();
        clone.RandomSeed = 2;
        clone.AgentCount = 20;

        Assert.NotSame(original, clone);
        Assert.Equal(1, original.RandomSeed);
        Assert.Equal(10, original.AgentCount);
    }
}
