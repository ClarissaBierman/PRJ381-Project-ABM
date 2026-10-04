using System.Globalization;
using System.Text;

namespace AbmFramework.Engine;

// Writes one summary row per Monte Carlo replicate: its seed, final tick's
// SIR counts (0 for non-SIR models) and every metric key seen across any
// replicate's final statistics (0 where a given replicate didn't report
// that key). Streams rows as it writes rather than holding everything in
// memory, per the project's guidance to use streaming CSV output for
// batches beyond roughly 100 replicates.
public static class MonteCarloCsvExporter
{
    public static async Task ExportAsync(
        IReadOnlyList<MonteCarloReplicateResult> results,
        string filePath)
    {
        var metricKeys = results
            .SelectMany(r => r.FinalStatistics.Metrics.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        var builder = new StringBuilder();

        builder.Append("ReplicateIndex,Seed,FinalTick,Susceptible,Infected,Recovered,TotalPopulation");
        foreach (var key in metricKeys)
        {
            builder.Append(',').Append(key);
        }
        builder.AppendLine();

        foreach (var result in results)
        {
            var stats = result.FinalStatistics;

            builder.Append(result.ReplicateIndex).Append(',')
                   .Append(result.Seed).Append(',')
                   .Append(stats.Tick).Append(',')
                   .Append(stats.Susceptible).Append(',')
                   .Append(stats.Infected).Append(',')
                   .Append(stats.Recovered).Append(',')
                   .Append(stats.TotalPopulation);

            foreach (var key in metricKeys)
            {
                var value = stats.Metrics.TryGetValue(key, out var v) ? v : 0.0;
                // Invariant culture so decimals always use '.', even on
                // machines whose locale uses ',' (e.g. en-ZA), which would
                // otherwise split each value across two CSV columns.
                builder.Append(',').Append(value.ToString("F2", CultureInfo.InvariantCulture));
            }

            builder.AppendLine();
        }

        await File.WriteAllTextAsync(filePath, builder.ToString());
    }
}
