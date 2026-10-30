// =============================================================================
// CsvExporter.cs  (ABM.Persistence.Export)
//
// Writes a run's tick history as CSV (comma-separated values), which opens in
// Excel, Google Sheets, Python (pandas) or R. One row per tick.
// Used by the dashboard's Download > CSV button and by the console app.
// =============================================================================

using System.Globalization;
using System.Text;
using ABM.Persistence.Models;

namespace ABM.Persistence.Export
{
    /// <summary>Writes tick statistics as CSV.</summary>
    public static class CsvExporter
    {
        /// <summary>Writes the ticks to a file, replacing it if it exists.</summary>
        public static async Task ExportTicksAsync(
            IEnumerable<TickRecord> ticks,
            string filePath)
        {
            using var writer = new StreamWriter(filePath);
            await WriteTicksAsync(ticks, writer);
        }

        /// <summary>
        /// Writes the ticks to any TextWriter (a file, or the web response for a download).
        /// SIR runs get Tick,Susceptible,Infected,Recovered. Runs whose ticks
        /// carry metrics (every other model) get Tick plus one column per
        /// metric key instead, since their S/I/R counts are always 0.
        /// </summary>
        public static async Task WriteTicksAsync(
            IEnumerable<TickRecord> ticks,
            TextWriter writer)
        {
            var records = ticks.ToList();

            // Every metric name used in the run, in a fixed (alphabetical) order.
            var metricKeys = records
                .SelectMany(r => r.Metrics.Keys)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();

            if (metricKeys.Count == 0)
            {
                builder.AppendLine("Tick,Susceptible,Infected,Recovered");

                foreach (var tick in records)
                {
                    builder.AppendLine(
                        $"{tick.TickNumber}," +
                        $"{tick.Susceptible}," +
                        $"{tick.Infected}," +
                        $"{tick.Recovered}");
                }
            }
            else
            {
                builder.AppendLine("Tick," + string.Join(",", metricKeys));

                foreach (var tick in records)
                {
                    builder.Append(tick.TickNumber);
                    foreach (var key in metricKeys)
                    {
                        // Invariant culture so decimals always use '.', even
                        // on machines whose locale uses ',' (e.g. en-ZA).
                        var value = tick.Metrics.TryGetValue(key, out var v) ? v : 0.0;
                        builder.Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
                    }
                    builder.AppendLine();
                }
            }

            await writer.WriteAsync(builder.ToString());
        }
    }
}
