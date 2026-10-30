// =============================================================================
// MonteCarloCsvExporter.cs  (AbmFramework.Engine)
//
// Writes a Monte Carlo batch to a CSV file with one row per run: its index,
// seed, final tick, the final SIR counts (0 for other models) and one column
// for every metric any run reported (0 where a run didn't report it).
//
// Used by the ConfigDemo console app. The dashboard has its own exporters in
// ABM.Persistence/Export.
//
// Note: the whole file is built in memory and written in one go. That is fine
// for hundreds of runs. For very large batches, writing row by row with a
// StreamWriter would use less memory.
// =============================================================================

using System.Globalization;
using System.Text;

namespace AbmFramework.Engine;

/// <summary>Saves Monte Carlo results as a CSV file, one row per run.</summary>
public static class MonteCarloCsvExporter
{
    /// <summary>Writes the results to <paramref name="filePath"/>, replacing the file if it exists.</summary>
    /// <param name="results">The runs to write, usually from <see cref="MonteCarloRunner.RunAsync"/>.</param>
    /// <param name="filePath">Where to save the CSV.</param>
    public static async Task ExportAsync(
        IReadOnlyList<MonteCarloReplicateResult> results,
        string filePath)
    {
        // Every metric name that appears in any run, sorted, so the columns
        // are the same for every row.
        var metricKeys = results
            .SelectMany(r => r.FinalStatistics.Metrics.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        var builder = new StringBuilder();

        // Header row.
        builder.Append("ReplicateIndex,Seed,FinalTick,Susceptible,Infected,Recovered,TotalPopulation");
        foreach (var key in metricKeys)
        {
            builder.Append(',').Append(key);
        }
        builder.AppendLine();

        // One row per run.
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
